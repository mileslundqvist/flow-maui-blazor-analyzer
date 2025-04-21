using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Concurrent;

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


    public InterproceduralCFG(Compilation compilation, IEnumerable<ICFGNode> initialEntryNodes)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));

        var list = new List<ICFGNode>();
        foreach (var entry in initialEntryNodes)
        {
            AddNodeInternal(entry);
            list.Add(entry);
            _entryMap.TryAdd(entry.MethodContext.MethodSymbol, entry);
            _methodContextCache.TryAdd(entry.MethodContext.MethodSymbol, entry.MethodContext);
        }
        EntryNodes = list;
    }

    // -- Public API --

    public IEnumerable<ICFGNode> Nodes => _nodes.Keys;

    public IEnumerable<ICFGEdge> GetOutgoingEdges(ICFGNode node)
    {
        EnsureSuccessorsComputed(node);
        return _successors.TryGetValue(node, out var edges) ? edges : Enumerable.Empty<ICFGEdge>();
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

    private void ComputeSuccessors(ICFGNode node)
    {
        // 1. Exit node: Add return edges to callers
        if (node.Kind == ICFGNodeKind.Exit)
        {
            foreach (var (callSite, returnNode) in _callers.GetOrAdd(node.MethodContext.MethodSymbol, _ => new()))
            {
                AddEdgeInternal(node, returnNode, EdgeType.Return);
            }
            return;
        }

        // 2. Obtain CFG lazily
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

        var block = cfg.Blocks.FirstOrDefault(b => b.Operations.Contains(node.Operation));
        if (block == null) return;

        // 4. Intraprocedural successors
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
            if (succ.Destination.Kind == BasicBlockKind.Exit)
            {
                var exitNode = FindOrCreateNode(null, node.MethodContext, ICFGNodeKind.Exit);
                AddEdgeInternal(node, exitNode, EdgeType.Intraprocedural);
            }



        }

        // - Call / Call-To-Return
        foreach (var inv in node.Operation.DescendantsAndSelf().OfType<IInvocationOperation>())
        {
            var callee = inv.TargetMethod;
            if (callee.IsAbstract || callee.IsExtern) continue;
            var calleeEntry = GetOrAddEntryNode(callee);
            AddEdgeInternal(node, calleeEntry, EdgeType.Call);

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
                AddEdgeInternal(node, returnNode, EdgeType.CallToReturn);
                _callers.GetOrAdd(callee, _ => new()).Add((node, returnNode));
            }
        }
    }

    // -- Helper methods --
    private MethodAnalysisContext GetOrAddContext(IMethodSymbol m)
        => _methodContextCache.GetOrAdd(m, ms => new MethodAnalysisContext(ms));

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
        if (operation is IExpressionStatementOperation) kind = ICFGNodeKind.CallSite;

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
        if (_entryMap.TryGetValue(m, out var node)) return node;
        var context = GetOrAddContext(m);
        var entry = new ICFGNode(null, context, ICFGNodeKind.Entry);
        AddNodeInternal(entry);
        _entryMap[m] = entry;
        return entry;
    }

}
