using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class InterproceduralCFG : IInterproceduralCFG<ICFGNode, IMethodSymbol>
{
    private readonly Compilation _compilation;
    private readonly Project _project;

    // -- Core Storage --
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _successors = new();
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _predecessors = new();
    private readonly ConcurrentDictionary<ICFGNode, bool> _nodes = new();
    private readonly ConcurrentDictionary<ICFGNode, Lazy<Task>> _successorsComputedTasks = new();

    // Entry point(s)
    public IReadOnlyCollection<ICFGNode> EntryNodes { get; }

    // Caches
    private readonly ConcurrentDictionary<IMethodSymbol, ICFGNode> _entryMap = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, List<(ICFGNode, ICFGNode)>> _callers = new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<ControlFlowGraph?>> _cfgCache = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, MethodAnalysisContext> _methodContextCache = new(SymbolEqualityComparer.Default);


    public InterproceduralCFG(Project project, Compilation compilation, IEnumerable<IMethodSymbol> initialMethodSymbols)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        var entryNodesList = new List<ICFGNode>();

        ArgumentNullException.ThrowIfNull(initialMethodSymbols);


        foreach (var methodSymbol in initialMethodSymbols)
        {
            var entryNode = GetOrAddEntryNode(methodSymbol);
            entryNodesList.Add(entryNode);

            //AddNodeInternal(entry);
            //list.Add(entry);
            //_entryMap.TryAdd(entry.MethodContext.MethodSymbol, entry);
            //_methodContextCache.TryAdd(entry.MethodContext.MethodSymbol, entry.MethodContext);
        }
        EntryNodes = entryNodesList.AsReadOnly();
    }

    // -- Public API --

    public IEnumerable<ICFGNode> Nodes => _nodes.Keys;

    public async Task<IEnumerable<ICFGEdge>> GetOutgoingEdges(ICFGNode node)
    {
        await EnsureSuccessorsComputed(node);
        return _successors.TryGetValue(node, out var edges) ? edges : Enumerable.Empty<ICFGEdge>();
    }

    public bool TryGetEntryNode(IMethodSymbol methodSymbol, [NotNullWhen(true)] out ICFGNode? entryNode)
    {
        ArgumentNullException.ThrowIfNull(methodSymbol);
        // Directly access the internal cache where entry nodes are stored by method symbol
        return _entryMap.TryGetValue(methodSymbol, out entryNode);
    }

    public IEnumerable<ICFGEdge> GetIncomingEdges(ICFGNode node)
    {
        return _predecessors.TryGetValue(node, out var edges) ? edges : Enumerable.Empty<ICFGEdge>();
    }

    public ICFGNode GetEntryNode(ICFGNode anyNode)
    {
        if (_entryMap.TryGetValue(anyNode.MethodContext.MethodSymbol, out var en)) return en;

        var context = GetOrAddContext(anyNode.MethodContext.MethodSymbol);
        var entry = new ICFGNode(null, context, ICFGNodeKind.Entry);
        AddNodeInternal(entry);
        _entryMap[context.MethodSymbol] = entry;
        return entry;
    }

    public ICFGEdge? TryGetCallEdge(ICFGNode callSite, ICFGNode calleeEntry)
    {
        EnsureSuccessorsComputed(callSite);
        return _successors.TryGetValue(callSite, out var list)
            ? list.FirstOrDefault(e => e.Type == EdgeType.Call && e.To.Equals(calleeEntry))
            : null;
    }

    // -- Core Demand-Driven successor exapansion --

    private async Task EnsureSuccessorsComputed(ICFGNode node)
    {
        var lazyTask = _successorsComputedTasks.GetOrAdd(node,
            (key) => new Lazy<Task>(() => ComputeSuccessors(key))
        );

        try
        {
            await lazyTask.Value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error computing successors for {node}: {ex}");
            // Potentially mark node as failed? Remove from cache?
            _successorsComputedTasks.TryRemove(node, out _); // Example: Allow retry on next call
                                                             // Re-throw or handle as appropriate for your analyzer's overall error strategy
            throw;
        }
    }

    private async Task ComputeSuccessors(ICFGNode node)
    {
        // 1. Exit node: Add return edges to callers
        if (node.Kind == ICFGNodeKind.Exit)
        {
            // Find callers for this method. If none exist yet, GetOrAdd provides an empty list.
            if (_callers.TryGetValue(node.MethodContext.MethodSymbol, out var callersList))
            {
                foreach (var (callSite, returnNode) in callersList)
                {
                    // Add a Return edge from the method's exit node to the call site's return node.
                    AddEdgeInternal(node, returnNode, EdgeType.Return);
                }
            }
            return; // No other successors from an Exit node
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
            var firstOperation = cfg.Blocks
                .Skip(1)
                .SelectMany(b => b.Operations)
                .FirstOrDefault();
            if (firstOperation != null)
            {
                var firstNode = FindOrCreateNode(firstOperation, node.MethodContext);
                AddEdgeInternal(node, firstNode, EdgeType.Intraprocedural);
            }
            else
            {
                var exitNode = FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                AddEdgeInternal(node, exitNode, EdgeType.Intraprocedural);
            }
            return;
        }

        // Should not happen for non-entry
        if (node.Operation is null) return;

        // Find the basic block containing the node's operation
        BasicBlock? block = cfg.Blocks.FirstOrDefault(b => b.Operations.Contains(node.Operation));
        if (block is null) return;

        // --- Determine Successors ---
        var invocations = node.Operation.DescendantsAndSelf().OfType<IInvocationOperation>().ToList();
        // Check if this node's primary operation is a call site or contains calls
        bool isOrContainsCall = invocations.Any();

        // Calculate the return site node
        ICFGNode? nextNodeAfterCurrentOp = CalculateReturnSiteNode(block, node);

        if (isOrContainsCall)
        {
            // --- Handle node as a Call Site ---

            // Add the CallToReturn edge regardless of whether implementations are found
            if (nextNodeAfterCurrentOp is not null)
            {
                AddEdgeInternal(node, nextNodeAfterCurrentOp, EdgeType.CallToReturn);
            }


            foreach (var inv in invocations)
            {
                IMethodSymbol? targetMethod = inv.TargetMethod;
                if (targetMethod is null) continue;

                // Determine potential concrete callees
                IEnumerable<IMethodSymbol> potentialCallees;
                if (targetMethod.IsAbstract || targetMethod.IsVirtual || targetMethod.ContainingType.TypeKind == TypeKind.Interface)
                {
                    potentialCallees = await FindImplementationsAsync(targetMethod).ConfigureAwait(false);
                }
                else if (!targetMethod.IsExtern)
                {
                    potentialCallees = new[] { targetMethod };
                }
                else
                {
                    potentialCallees = Enumerable.Empty<IMethodSymbol>();
                }

                // Add edges for each potential concrete callee
                foreach (var callee in potentialCallees)
                {
                    // Should be concrete if found via FindImplementationsAsync
                    if (callee.IsAbstract || callee.IsExtern) continue;

                    ICFGNode calleeEntry = GetOrAddEntryNode(callee);

                    // Add Call edge from call site (node) to callee entry
                    AddEdgeInternal(node, calleeEntry, EdgeType.Call);

                    // Add CallToReturn edge and record caller info (if return site exists)
                    if (nextNodeAfterCurrentOp != null)
                    {
                        // Ensure list exists and add the caller info
                        var callersList = _callers.GetOrAdd(callee, _ => new List<(ICFGNode, ICFGNode)>());

                        lock (callersList)
                        {
                            // Avoid adding duplicates if somehow processed twice (shouldn't happen with GetOrAdd logic)
                            if (!callersList.Contains((node, nextNodeAfterCurrentOp)))
                            {
                                callersList.Add((node, nextNodeAfterCurrentOp));
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // --- Handle node as Normal Intraprocedural Step ---
            // The successor is simply the calculated nextNodeAfterCurrentOp

            if (nextNodeAfterCurrentOp != null)
            {
                AddEdgeInternal(node, nextNodeAfterCurrentOp, EdgeType.Intraprocedural);
            }
            else
            {
                // This might happen if CalculateReturnSiteNode couldn't find a successor
                // (e.g., end of method, throw without catch). Often leads to Exit node implicitly.
                Console.WriteLine($"Warning: No intraprocedural successor found for non-call node: {node}");
                // Optionally add edge to Exit node as fallback?
                // var exitNode = FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                // AddEdgeInternal(node, exitNode, EdgeType.Intraprocedural);
            }
        }
    }

    // -- Helper methods --
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
        IOperation? nextOperationInBlock = (operationIndex >= 0 && operationIndex < block.Operations.Length - 1)
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
                (operation is ISimpleAssignmentOperation assignOp &&
                (assignOp.Value is IInvocationOperation || assignOp.Value is IAwaitOperation)) ||
                (operation is IExpressionStatementOperation exprOp && (exprOp.Operation is IInvocationOperation ||
                (exprOp.Operation is ISimpleAssignmentOperation innerAssignOp && innerAssignOp.Value is IInvocationOperation))))
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
