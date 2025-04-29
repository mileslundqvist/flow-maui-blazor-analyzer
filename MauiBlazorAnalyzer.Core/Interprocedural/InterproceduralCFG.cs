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

    // -- Core Storage --
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _successors = new();
    private readonly ConcurrentDictionary<ICFGNode, List<ICFGEdge>> _predecessors = new();
    private readonly ConcurrentDictionary<ICFGNode, bool> _nodes = new();
    private readonly ConcurrentDictionary<ICFGNode, bool> _successorsComputed = new();

    // Entry point(s)
    public IReadOnlyCollection<ICFGNode> EntryNodes { get; }

    // Caches
    private readonly ConcurrentDictionary<IMethodSymbol, ICFGNode> _entryMap = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, List<(ICFGNode, ICFGNode)>> _callers = new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<ControlFlowGraph?>> _cfgCache = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, MethodAnalysisContext> _methodContextCache = new(SymbolEqualityComparer.Default);


    public InterproceduralCFG(Compilation compilation, IEnumerable<IMethodSymbol> initialMethodSymbols)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
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

    public IEnumerable<ICFGEdge> GetOutgoingEdges(ICFGNode node)
    {
        EnsureSuccessorsComputed(node);
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

    private void EnsureSuccessorsComputed(ICFGNode node)
    {
        _successorsComputed.GetOrAdd(node, node => { ComputeSuccessors(node); return true; });
    }

    private async void ComputeSuccessors(ICFGNode node)
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
        var cfg = GetCfg(node.MethodContext.MethodSymbol);
        if (cfg is null) return;

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

        if (node.Operation is null) return; // Should not happen for non-entry

        // Find the basic block containing the node's operation
        var block = cfg.Blocks.FirstOrDefault(b => b.Operations.Contains(node.Operation));
        if (block == null) return;

        // Detect Call Site
        bool isCallSiteNode = node.Kind == ICFGNodeKind.CallSite ||
                               node.Operation is IInvocationOperation ||
                               (node.Operation is ISimpleAssignmentOperation assignOp && assignOp.Value is IInvocationOperation) ||
                               (node.Operation is IExpressionStatementOperation exprOp && (exprOp.Operation is IInvocationOperation || 
                               (exprOp.Operation is ISimpleAssignmentOperation innerAssignOp && innerAssignOp.Value is IInvocationOperation)));


        // 4. Intraprocedural successors
        if (!isCallSiteNode)
        {
            int operationIndex = Array.IndexOf(block.Operations.ToArray(), node.Operation);

            // If there is a next operation within the same block, add an edge to it
            if (operationIndex >= 0 && operationIndex < block.Operations.Length - 1)
            {
                var nextOperationInBlock = block.Operations[operationIndex + 1];
                var nextNode = FindOrCreateNode(nextOperationInBlock, node.MethodContext);
                AddEdgeInternal(node, nextNode, EdgeType.Intraprocedural);
            }
            else
            {
                // If this is the last operation in the block, add edge to successor blocks
                foreach (var succ in new[] { block.ConditionalSuccessor, block.FallThroughSuccessor })
                {
                    if (succ?.Destination is null) continue;

                    var targetOp = succ?.Destination?.Operations.FirstOrDefault();
                    if (targetOp is not null)
                    {
                        var succNode = FindOrCreateNode(targetOp, node.MethodContext);
                        AddEdgeInternal(node, succNode, EdgeType.Intraprocedural);
                    }

                    // Edge into synthetic exit block
                    else if (succ.Destination.Kind == BasicBlockKind.Exit)
                    {
                        var exitNode = FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                        AddEdgeInternal(node, exitNode, EdgeType.Intraprocedural);
                    }
                }
            }
        }
        

        // - Call / Call-To-Return
        foreach (var inv in node.Operation.DescendantsAndSelf().OfType<IInvocationOperation>())
        {
            var targetMethod = inv.TargetMethod;
            if (targetMethod is null) continue;

            IEnumerable<IMethodSymbol> potentialCallees;


            // If the method is abstract, virtual, or an interface method, we need to handle it differently
            if (targetMethod.IsAbstract || targetMethod.IsVirtual || targetMethod.ContainingType.TypeKind == TypeKind.Interface)
            {
                potentialCallees = await FindImplementationsAsync(targetMethod);
            }
            else if (!targetMethod.IsExtern)
            {
                // Handle direct non-extern calls
                potentialCallees = new[] { targetMethod };
            }
            else
            {
                potentialCallees = Enumerable.Empty<IMethodSymbol>();
            }







                var calleeEntry = GetOrAddEntryNode(targetMethod);






            // Add a Call edge from the current node (the call site) to the callee's entry node
            AddEdgeInternal(node, calleeEntry, EdgeType.Call);

            // Find the return side node: the program point immediately after the call site
            ICFGNode? returnNode = null;
            var ops = block.Operations;

            int index = Array.IndexOf(ops.ToArray(), node.Operation);
            if (index >= 0 && index + 1 < ops.Length)
            {
                var nextOperation = ops[index + 1];
                returnNode = FindOrCreateNode(nextOperation, node.MethodContext);
            }
            else
            {
                var fallOperation = block.FallThroughSuccessor?.Destination?.Operations.FirstOrDefault();
                if (fallOperation != null)
                {
                    returnNode = FindOrCreateNode(fallOperation, node.MethodContext);
                }
                else if (block.FallThroughSuccessor?.Destination?.Kind == BasicBlockKind.Exit)
                {
                    returnNode = FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                }
            }

            if (returnNode != null)
            {
                // Add a CallToReturn edge from the current node (the call site) to the return site node.
                AddEdgeInternal(node, returnNode, EdgeType.CallToReturn);

                // Record the call site node and its return node for Return edge generation later by the callee's Exit node.
                // GetOrAdd is used for thread safety, ensuring the list exists.
                _callers.GetOrAdd(targetMethod, _ => new()).Add((node, returnNode));
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
                (operation is ISimpleAssignmentOperation assignOp && assignOp.Value is IInvocationOperation) ||
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
                // TODO: Get real solution
                var implementingTypes = await SymbolFinder.FindImplementationsAsync(baseMethod.ContainingType, solution: null);
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
                var overridingMethods = await SymbolFinder.FindOverridesAsync(baseMethod, solution: null, null, CancellationToken.None);
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
