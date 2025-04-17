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

    // -- State for Demand-Drive Computation --
    private readonly ConcurrentDictionary<ICFGNode, bool> _successorsComputed = new();

    // Entry point(s)
    public List<ICFGNode> EntryNodes { get; } = new();


    public InterproceduralCFG(Compilation compilation, IEnumerable<ICFGNode> initialEntryNodes)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        foreach (var entry in initialEntryNodes)
        {
            AddNodeInternal(entry);
            EntryNodes.Add(entry);
        }
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

    // -- Core Demand-Driven Logic --

    private void EnsureSuccessorsComputed(ICFGNode node)
    {
        // Check if already compute. If so return immediately.
        // AddOrUpdate ensures thread-safety. The value factory
        // is used if the key does not exist.
        _successorsComputed.AddOrUpdate(node,
            (key) => { ComputeSuccessors(key); return true; },
            (key, existingValue) => existingValue
            );
    }

    private void ComputeSuccessors(ICFGNode node)
    {
        // 1. Necessary Roslyn Context
        SemanticModel semanticModel = node.Operation.SemanticModel;
        if (semanticModel == null)
        {
            // TODO: Log error
            _successors.TryAdd(node, new List<ICFGEdge>());
            return;
        }

        _successors.TryAdd(node, new List<ICFGEdge>());

        // 2. Determine Successors based on node kind and operation

        if (node.Kind == ICFGNodeKind.Exit)
        {
            return;
        }

        if (node.Operation == null && node.Kind != ICFGNodeKind.Entry)
        {
            // Should not happen for non-Entry/Exit nodes
            return;
        }

        // -- Intraprocedural Successors (Normal Flow) --
        ControlFlowGraph cfg = node.MethodContext.ControlFlowGraph;

        BasicBlock? currentBlock = cfg?.Blocks.FirstOrDefault(bb => bb.Operations.Contains(node.Operation));

        if (currentBlock != null)
        {
            if (currentBlock.ConditionalSuccessor != null)
            {
                var targetOp = currentBlock.ConditionalSuccessor.Destination!.Operations.FirstOrDefault();
                if (targetOp != null)
                {
                    var succesorNode = FindOrCreateNode(targetOp, node.MethodContext, semanticModel);
                    AddEdgeInternal(node, succesorNode, EdgeType.Intraprocedural);
                }
            }

            if (currentBlock.FallThroughSuccessor != null)
            {
                var targetOp = currentBlock.FallThroughSuccessor.Destination!.Operations.FirstOrDefault();
                if (targetOp != null)
                {
                    var succesorNode = FindOrCreateNode(targetOp, node.MethodContext, semanticModel);
                    AddEdgeInternal(node, succesorNode, EdgeType.Intraprocedural);
                }
            }
        }
        else if (node.Kind == ICFGNodeKind.Entry)
        {
            var firstOperation = cfg?.Blocks.FirstOrDefault()?.Operations.FirstOrDefault();
            if (firstOperation != null)
            {
                var firstNode = FindOrCreateNode(firstOperation, node.MethodContext, semanticModel);
                AddEdgeInternal(node, firstNode, EdgeType.Intraprocedural);
            }
        }

        if (node.Kind == ICFGNodeKind.CallSite && 
            node.Operation is IExpressionStatementOperation expressionStatementOperation)
        {
            // a) Call-to-Return Edge

            // b) Call Edge (to callee entry)
        }

    }

    // -- Helper methods --

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

    private ICFGNode FindOrCreateNode(IOperation? operation, MethodAnalysisContext context, SemanticModel model, ICFGNodeKind kindHint = ICFGNodeKind.Normal)
    {
        ICFGNodeKind kind = kindHint;
        if (operation is IExpressionStatementOperation) kind = ICFGNodeKind.CallSite;

        var potentialNode = new ICFGNode(operation, context, kind);

        ICFGNode existingNode = _nodes.Keys.FirstOrDefault(n => n.Equals(potentialNode));

        if (existingNode != null)
        {
            return existingNode;
        }
        else
        {
            AddNodeInternal(potentialNode);
            return potentialNode;
        }
    }

    // Placeholder for complex parts - IMPLEMENT THESE WITH DETAILED ROSLYN LOGIC
    private void ComputeReturnEdges(ICFGNode exitNode, SemanticModel model) { /* Complex: Find callers and link to return sites */ }
    private void ComputeCallEdges(ICFGNode callSiteNode, IInvocationOperation invocation, SemanticModel model) { /* Implemented partially above, needs refinement */ }
    private IOperation? FindNextOperationAfter(IInvocationOperation invocation) { /* Helper to find subsequent operation */ return null; }
    private MethodAnalysisContext GetOrCreateMethodContext(IMethodSymbol targetMethod, Compilation compilation) { /* Helper to manage contexts */ throw new NotImplementedException(); }
}
