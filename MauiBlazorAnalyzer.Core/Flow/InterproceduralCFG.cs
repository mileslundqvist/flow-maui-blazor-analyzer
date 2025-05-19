using MauiBlazorAnalyzer.Core.Flow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Flow;
public class InterproceduralCFG : IInterproceduralCFG<ICFGNode, IMethodSymbol>
{
    private readonly Compilation _compilation;
    private readonly Project _project;
    private readonly CancellationToken _cancellationToken;

    // Core Storage
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _successors = new();
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _predecessors = new();
    private readonly ConcurrentDictionary<ICFGNode, bool> _nodes = new();
    private readonly ConcurrentDictionary<ICFGNode, Lazy<Task>> _successorsComputationTasks = new();

    // Entry point(s)
    public IReadOnlyCollection<ICFGNode> EntryNodes { get; }

    // Caches
    private readonly ConcurrentDictionary<IMethodSymbol, ICFGNode> _entryMap = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, List<(ICFGNode, ICFGNode)>> _callers = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<ControlFlowGraph?>> _cfgCache = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, MethodAnalysisContext> _methodContextCache = new(SymbolEqualityComparer.Default);


    public InterproceduralCFG(
        Project project, 
        Compilation compilation, 
        IEnumerable<IMethodSymbol> initialMethodSymbols,
        CancellationToken cancellationToken = default)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _cancellationToken = cancellationToken;

        ArgumentNullException.ThrowIfNull(initialMethodSymbols);

        var entryNodesList = new List<ICFGNode>();
        foreach (var methodSymbol in initialMethodSymbols)
        {
            var entryNode = GetOrAddEntryNode(methodSymbol);
            entryNodesList.Add(entryNode);

        }

        EntryNodes = entryNodesList.AsReadOnly();
    }

    // -- Public API --

    public IEnumerable<ICFGNode> Nodes => _nodes.Keys;

    public async Task<IEnumerable<ICFGEdge>> GetOutgoingEdgesAsync(ICFGNode node, CancellationToken cancellationToken = default)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

        await EnsureSuccessorsComputedAsync(node, combinedCts.Token).ConfigureAwait(false);
        return _successors.TryGetValue(node, out var edges) ? edges : Enumerable.Empty<ICFGEdge>();
    }

    public bool TryGetEntryNode(IMethodSymbol methodSymbol, [NotNullWhen(true)] out ICFGNode? entryNode)
    {
        ArgumentNullException.ThrowIfNull(methodSymbol);
        return _entryMap.TryGetValue(methodSymbol, out entryNode);
    }

    public IEnumerable<ICFGEdge> GetIncomingEdges(ICFGNode node)
    {
        return _predecessors.TryGetValue(node, out var edges) ? edges : Enumerable.Empty<ICFGEdge>();
    }

    public ICFGNode GetEntryNode(ICFGNode anyNode)
    {
        if (_entryMap.TryGetValue(anyNode.MethodContext.MethodSymbol, out var existingEntry)) 
            return existingEntry;

        var context = GetOrAddContext(anyNode.MethodContext.MethodSymbol);
        var entry = new ICFGNode(null, context, ICFGNodeKind.Entry);
        AddNodeInternal(entry);
        _entryMap[context.MethodSymbol] = entry;
        return entry;
    }

    public async Task<ICFGEdge?> TryGetCallEdge(ICFGNode callSite, ICFGNode calleeEntry, CancellationToken cancellationToken = default)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

        await EnsureSuccessorsComputedAsync(callSite, combinedCts.Token).ConfigureAwait(false);
        return _successors.TryGetValue(callSite, out var list)
            ? list.FirstOrDefault(e => e.Type == EdgeType.Call && e.To.Equals(calleeEntry))
            : null;
    }

    // -- Core Demand-Driven successor exapansion --

    private async Task EnsureSuccessorsComputedAsync(ICFGNode node, CancellationToken cancellationToken)
    {
        var lazyTask = _successorsComputationTasks.GetOrAdd(node,
            (key) => new Lazy<Task>(() => ComputeSuccessorsAsync(key, cancellationToken))
        );

        try
        {
            await lazyTask.Value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error computing successors for {node}: {ex}");

            _successorsComputationTasks.TryRemove(node, out _);
            throw;
        }
    }


    private async Task ComputeSuccessorsAsync(ICFGNode node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Exit node: Add return edges to callers
        if (node.Kind == ICFGNodeKind.Exit)
        {
            await ComputeExitNodeSuccessorsAsync(node).ConfigureAwait(false);
            return;
        }

        // 2. Obtain CFG lazily for non-exit nodes
        ControlFlowGraph? cfg = GetCfg(node.MethodContext.MethodSymbol);
        if (cfg is null)
        {
            _successors.TryAdd(node, new List<ICFGEdge>());
            return;
        }

        // Ensure successor list exists for this node
        _successors.TryAdd(node, new List<ICFGEdge>());

        // 3. Entry node -> first real operation
        if (node.Kind == ICFGNodeKind.Entry)
        {
            ComputeEntryNodeSuccessors(node, cfg);
            return;
        }

        // Should not happen for non-entry
        if (node.Operation is null) return;

        await ComputeRegularNodeSuccessorsAsync(node, cfg, cancellationToken).ConfigureAwait(false);
    }

    private Task ComputeExitNodeSuccessorsAsync(ICFGNode exitNode)
    {
        if (_callers.TryGetValue(exitNode.MethodContext.MethodSymbol, out var callersList))
        {
            lock (callersList)
            {
                foreach (var (callSite, returnNode) in callersList)
                {
                    AddEdgeInternal(exitNode, returnNode, EdgeType.Return);
                }
            }
        }
        return Task.CompletedTask;
    }

    private void ComputeEntryNodeSuccessors(ICFGNode entryNode, ControlFlowGraph cfg)
    {
        var firstOperation = cfg.Blocks
            .Skip(1)
            .SelectMany(b => b.Operations)
            .FirstOrDefault();

        if (firstOperation != null)
        {
            var firstNode = FindOrCreateNode(firstOperation, entryNode.MethodContext);
            AddEdgeInternal(entryNode, firstNode, EdgeType.Intraprocedural);
        }
        else
        {
            var exitNode = FindOrCreateNode(null, entryNode.MethodContext, ICFGNodeKind.Exit);
            AddEdgeInternal(entryNode, exitNode, EdgeType.Intraprocedural);
        }
    }


    private async Task ComputeRegularNodeSuccessorsAsync(ICFGNode node, ControlFlowGraph cfg, CancellationToken cancellationToken)
    {
        // Find the basic block containing the node's operation
        var block = cfg.Blocks.FirstOrDefault(b => b.Operations.Contains(node.Operation!));
        if (block is null) return;

        var invocations = node.Operation!.DescendantsAndSelf().OfType<IInvocationOperation>().ToList();
        var isCallSite = invocations.Any();
        var nextNode = CalculateReturnSiteNode(block, node);

        if (isCallSite)
        {
            await ComputeCallSiteSuccessorsAsync(node, invocations, nextNode, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ComputeNormalNodeSuccessors(node, nextNode);
        }
    }

    private async Task ComputeCallSiteSuccessorsAsync(
        ICFGNode callSite,
        IList<IInvocationOperation> invocations,
        ICFGNode? returnNode,
        CancellationToken cancellationToken)
    {
        // Add CallToReturn edge regardless of whether implementations are found
        if (returnNode != null)
        {
            AddEdgeInternal(callSite, returnNode, EdgeType.CallToReturn);
        }

        foreach (var invocation in invocations)
        {
            if (invocation.TargetMethod is null) continue;

            var potentialCallees = await FindPotentialCalleesAsync(invocation.TargetMethod, cancellationToken).ConfigureAwait(false);

            foreach (var callee in potentialCallees)
            {
                if (callee.IsAbstract || callee.IsExtern) continue;

                var calleeEntry = GetOrAddEntryNode(callee);
                AddEdgeInternal(callSite, calleeEntry, EdgeType.Call);

                // Record caller information for later return edge creation
                if (returnNode != null)
                {
                    RecordCallerInfo(callee, callSite, returnNode);
                }
            }
        }
    }

    private void ComputeNormalNodeSuccessors(ICFGNode node, ICFGNode? nextNode)
    {
        if (nextNode != null)
        {
            AddEdgeInternal(node, nextNode, EdgeType.Intraprocedural);
        }
    }

    private void RecordCallerInfo(IMethodSymbol callee, ICFGNode callSite, ICFGNode returnNode)
    {
        var callersList = _callers.GetOrAdd(callee, _ => new List<(ICFGNode, ICFGNode)>());

        lock (callersList)
        {
            var callerInfo = (callSite, returnNode);
            if (!callersList.Contains(callerInfo))
            {
                callersList.Add(callerInfo);
            }
        }
    }

    // -- Helper methods --

    private async Task<IEnumerable<IMethodSymbol>> FindPotentialCalleesAsync(IMethodSymbol targetMethod, CancellationToken cancellationToken)
    {
        if (!ShouldFindImplementations(targetMethod))
        {
            return targetMethod.IsExtern ? Enumerable.Empty<IMethodSymbol>() : new[] { targetMethod };
        }

        try
        {
            return await FindImplementationsAsync(targetMethod).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            // Log error but continue execution
            Console.Error.WriteLine($"Error finding implementations for {targetMethod.Name}: {ex.Message}");
            return Enumerable.Empty<IMethodSymbol>();
        }
    }

    private static bool ShouldFindImplementations(IMethodSymbol method) =>
        method.IsAbstract || method.IsVirtual || method.ContainingType.TypeKind == TypeKind.Interface;


    private MethodAnalysisContext GetOrAddContext(IMethodSymbol m)
        => _methodContextCache.GetOrAdd(m, sym => new MethodAnalysisContext(sym));

    private void AddNodeInternal(ICFGNode node)
    {
        _nodes.TryAdd(node, true);
        _successors.TryAdd(node, new List<ICFGEdge>());
        _predecessors.TryAdd(node, new List<ICFGEdge>());
    }

    private void AddEdgeInternal(ICFGNode from, ICFGNode to, EdgeType edgeType)
    {
        AddNodeInternal(from);
        AddNodeInternal(to);

        var edge = new ICFGEdge(from, to, edgeType);

        var successorList = _successors.GetOrAdd(from, (_) => new List<ICFGEdge>());
        lock (successorList)
        {
            if (!successorList.Contains(edge))
            {
                successorList.Add(edge);
            }
        }

        var predecessorList = _predecessors.GetOrAdd(to, (_) => new List<ICFGEdge>());
        lock (predecessorList)
        {
            if (!predecessorList.Contains(edge))
            {
                predecessorList.Add(edge);
            }
        }
    }


    private ICFGNode? CalculateReturnSiteNode(BasicBlock block, ICFGNode node)
    {
        int operationIndex = Array.IndexOf(block.Operations.ToArray(), node.Operation);
        IOperation? nextOperationInBlock = operationIndex >= 0 && operationIndex < block.Operations.Length - 1
                                           ? block.Operations[operationIndex + 1]
                                           : null;

        if (nextOperationInBlock != null)
        {
            return FindOrCreateNode(nextOperationInBlock, node.MethodContext);
        }
        else
        {
            BasicBlock? fallThroughDest = block.FallThroughSuccessor?.Destination;
            IOperation? fallThroughOp = fallThroughDest?.Operations.FirstOrDefault();

            if (fallThroughOp != null)
            {
                return FindOrCreateNode(fallThroughOp, node.MethodContext);
            }
            else if (fallThroughDest?.Kind == BasicBlockKind.Exit)
            {
                return FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
            }
            else
            {
                // Check conditional successor if fallthrough didn't yield a node
                BasicBlock? conditionalDest = block.ConditionalSuccessor?.Destination;
                IOperation? conditionalOp = conditionalDest?.Operations.FirstOrDefault();
                if (conditionalOp != null)
                {
                    return FindOrCreateNode(conditionalOp, node.MethodContext);
                }
                else if (conditionalDest?.Kind == BasicBlockKind.Exit)
                {
                    return FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                }
            }
        }
        return null;
    }

    private static bool ContainsInvocation(IOperation op)
    {
        if (op is IInvocationOperation) return true;
        foreach (var child in op.ChildOperations)
            if (ContainsInvocation(child)) return true;
        return false;
    }

    private static IInvocationOperation? ExtractInvocation(IOperation op)
    {
        if (op is IInvocationOperation inv) return inv;
        foreach (var child in op.ChildOperations)
        {
            var found = ExtractInvocation(child);
            if (found != null) return found;
        }
        return null;
    }

    private ICFGNode FindOrCreateNode(IOperation? operation, MethodAnalysisContext context, ICFGNodeKind kindHint = ICFGNodeKind.Normal)
    {
        ICFGNodeKind kind = kindHint;

        if (operation == null)
        {
            if (kindHint != ICFGNodeKind.Exit) throw new ArgumentNullException(nameof(operation), "Operation cannot be null for non-Exit nodes.");
            kind = ICFGNodeKind.Exit;
        }
        else
        {
            if (operation is IInvocationOperation ||
                operation is ISimpleAssignmentOperation assignOp &&
                (assignOp.Value is IInvocationOperation || assignOp.Value is IAwaitOperation) ||
                operation is IExpressionStatementOperation exprOp && (exprOp.Operation is IInvocationOperation ||
                exprOp.Operation is ISimpleAssignmentOperation innerAssignOp && innerAssignOp.Value is IInvocationOperation))
            {
                kind = ICFGNodeKind.CallSite;
            }
            else if (operation is IReturnOperation)
            {

            }
            else
            {
                kind = ICFGNodeKind.Normal;
            }
        }

        var candidate = new ICFGNode(operation, context, kind);

        return _nodes.Keys.FirstOrDefault(n => n.Equals(candidate)) ?? AddFresh(candidate);
    }

    private ICFGNode AddFresh(ICFGNode node)
    {
        AddNodeInternal(node);
        return node;
    }

    private ControlFlowGraph? GetCfg(IMethodSymbol m)
    {
        return _cfgCache.GetOrAdd(m, ms => new Lazy<ControlFlowGraph?>(() =>
        {
            var ctx = _methodContextCache.GetOrAdd(ms, sym => new MethodAnalysisContext(sym));
            var op = ctx.EnsureOperation(_compilation);
            return op == null ? null : CreateCfg(op);
        })).Value;
    }

    private ControlFlowGraph? CreateCfg(IOperation operation)
    {
        return operation switch
        {
            IMethodBodyOperation m => ControlFlowGraph.Create(m),
            IConstructorBodyOperation c => ControlFlowGraph.Create(c),
            _ => null,
        };
    }

    private ICFGNode GetOrAddEntryNode(IMethodSymbol m)
    {
        ArgumentNullException.ThrowIfNull(m);

        if (_entryMap.TryGetValue(m, out var node)) return node;

        var context = GetOrAddContext(m);
        var entry = new ICFGNode(null, context, ICFGNodeKind.Entry);
        AddNodeInternal(entry);
        _entryMap.TryAdd(m, entry);
        return entry;
    }

    private async Task<IEnumerable<IMethodSymbol>> FindImplementationsAsync(IMethodSymbol methodSymbol)
    {
        IMethodSymbol baseMethod = methodSymbol.OriginalDefinition;
        var implementations = new List<IMethodSymbol>();

        if (baseMethod.ContainingType.TypeKind == TypeKind.Interface)
        {
            try
            {
                var implementingTypes = await SymbolFinder.FindImplementationsAsync(baseMethod.ContainingType, _project.Solution);
                foreach (var typeSymbol in implementingTypes)
                {
                    var member = typeSymbol.FindImplementationForInterfaceMember(baseMethod);
                    if (member is IMethodSymbol implementingMethod && !implementingMethod.IsAbstract)
                    {
                        implementations.Add(implementingMethod);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error finding interface implementations for {baseMethod.ContainingType.Name}: {ex.Message}");
            }
        }
        else
        {
            // Virtual or Abstract method in a class
            try
            {
                var overridingMethods = await SymbolFinder.FindOverridesAsync(baseMethod, _project.Solution, null, CancellationToken.None);
                implementations.AddRange(overridingMethods.OfType<IMethodSymbol>().Where(m => !m.IsAbstract));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error finding overrides for {baseMethod.Name}: {ex.Message}");
            }

            if (baseMethod.IsVirtual && !baseMethod.IsAbstract)
            {
                implementations.Add(baseMethod);
            }
        }
        return implementations.Distinct(SymbolEqualityComparer.Default).Cast<IMethodSymbol>();
    }
}
