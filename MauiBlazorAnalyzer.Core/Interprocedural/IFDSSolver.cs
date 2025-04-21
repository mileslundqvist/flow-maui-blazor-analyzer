using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MauiBlazorAnalyzer.Core.Interprocedural;

public class IFDSSolver
{
    private readonly IFDSTabulationProblem _problem;
    private readonly InterproceduralCFG _graph;

    // path‑edges:   ⟨node,fact⟩  -> { ⟨predNode,predFact⟩ }
    private readonly Dictionary<(ICFGNode node, IFact fact), HashSet<(ICFGNode predNode, IFact predFact)>> _pathEdges = new();

    // summary‑edges: (calleeEntry, inFact) -> { outFact }  (cached after first run)
    private readonly Dictionary<(ICFGNode entry, IFact inFact), HashSet<TaintFact>> _summaryEdges = new();

    // analysis result map: node -> facts that reach it (excluding ZeroFact)
    private readonly Dictionary<ICFGNode, HashSet<TaintFact>> _analysisResults = new();

    private readonly Queue<(ICFGNode node, IFact fact)> _workQueue = new();
    private readonly HashSet<(ICFGNode node, IFact fact)> _inQueue = new();

    public IFDSSolver(IFDSTabulationProblem problem)
    {
        _problem = problem;
        _graph = problem.Graph;
    }

    public IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> Solve()
    {
        Initialize();
        ProcessWorklist();
        return _analysisResults.ToDictionary(kvp => kvp.Key, kvp => (ISet<TaintFact>)kvp.Value);
    }

    // Seeding
    private void Initialize()
    {
        _analysisResults.Clear();
        _workQueue.Clear();
        _inQueue.Clear();
        _pathEdges.Clear();
        _summaryEdges.Clear();

        var initial = _problem.InitialSeeds;
        foreach (var (node, facts) in initial)
        {
            foreach (var fact in facts)
            {
                Propagate(node, fact, node, fact);
            }
        }
    }

    private void ProcessWorklist()
    {
        while (_workQueue.Count > 0)
        {
            var (node, fact) = _workQueue.Dequeue();
            _inQueue.Remove((node, fact));

            foreach (var edge in _graph.GetOutgoingEdges(node))
            {
                // -- ZeroFact Handling --
                if (fact.Equals(_problem.ZeroValue))
                {
                    Propagate(edge.To, fact, node, fact);
                    continue; // skip user flow‑functions for ⊥
                }

                // -- Non‑zero facts (= TaintFact) --
                var tFact = (TaintFact)fact;
                ISet<TaintFact> successorFacts = new HashSet<TaintFact>(); // Initialize empty set

                switch (edge.Type)
                {
                    case EdgeType.Intraprocedural:
                        var normalFlow = _problem.FlowFunctions.GetNormalFlowFunction(edge, tFact);
                        successorFacts.UnionWith(normalFlow?.ComputeTargets(tFact) ?? new HashSet<TaintFact>());
                        break;

                    case EdgeType.Call:
                        successorFacts.UnionWith(HandleCall(edge, tFact));
                        break;

                    case EdgeType.Return:
                        successorFacts.UnionWith(HandleReturn(edge, tFact));
                        break;

                    case EdgeType.CallToReturn:
                        var ctrFlow = _problem.FlowFunctions.GetCallToReturnFlowFunction(edge, tFact);
                        successorFacts.UnionWith(ctrFlow?.ComputeTargets(tFact) ?? new HashSet<TaintFact>());
                        break;
                }

                // Propagate the calculated successor facts to the target node (edge.To)
                foreach (var succesorFact in successorFacts)
                {
                    Propagate(edge.To, succesorFact, node, tFact);
                }
            }
        }
    }

    // Call edge handling (non-zero facts)
    private ISet<TaintFact> HandleCall(ICFGEdge callEdge, TaintFact inFactAtCallsite)
    {
        var calleeEntry = _graph.GetEntryNode(callEdge.To);

        var callFlowFunction = _problem.FlowFunctions.GetCallFlowFunction(callEdge, inFactAtCallsite);
        var inFactsAtEntry = callFlowFunction?.ComputeTargets(inFactAtCallsite) ?? new HashSet<TaintFact>();

        if (_summaryEdges.TryGetValue((calleeEntry, inFactAtCallsite), out var cachedExitFacts))
        {
            // Summary found, propagate results directly to the return site

            var callToReturnEdgeTarget = _graph
                .GetOutgoingEdges(callEdge.From)
                .FirstOrDefault(e => e.Type == EdgeType.CallToReturn && e.From.Equals(callEdge.From))?.To;

            if (callToReturnEdgeTarget != null)
            {
                foreach (var exitFact in cachedExitFacts)
                {
                    var returnFunction = _problem.FlowFunctions.GetReturnFlowFunction(null, exitFact, inFactAtCallsite); // Pass null for edge, ReturnFlow might not need it
                    var returnSiteFacts = returnFunction?.ComputeTargets(exitFact) ?? new HashSet<TaintFact>();

                    foreach (var returnFact in returnSiteFacts)
                    {
                        Propagate(callToReturnEdgeTarget, returnFact, callEdge.From, inFactAtCallsite);
                    }
                }
            }
        }

        return inFactsAtEntry;

    }

    // Return edge handling (non-zero facts)
    private ISet<TaintFact> HandleReturn(ICFGEdge returnEdge, TaintFact exitFact)
    {
        var calleeExitNode = returnEdge.From;
        var calleeEntry = _graph.GetEntryNode(calleeExitNode);

        var predecessors = _pathEdges.TryGetValue((calleeExitNode, exitFact), out var p) ? p : Enumerable.Empty<(ICFGNode, IFact)>();
        var outSet = new HashSet<TaintFact>();

        foreach (var (callSite, callFactRaw) in predecessors)
        {
            if (callFactRaw is not TaintFact callFact) continue; // Ignore ZeroFact predecessors

            var returnFunction = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, exitFact, callFact);
            var targets = returnFunction?.ComputeTargets(exitFact) ?? new HashSet<TaintFact>();

            // Add results to the set of facts flowing back to the return site
            outSet.UnionWith(targets);

            var summaryKey = (calleeEntry, callFact);
            if (!_summaryEdges.TryGetValue(summaryKey, out var set))
            {
                _summaryEdges[summaryKey] = set = new HashSet<TaintFact>();
            }
            set.UnionWith(targets);
        }

        return outSet;
    }

    private void Propagate(ICFGNode targetNode, IFact targetFact, ICFGNode predecessorNode, IFact predecessorFact)
    {

        // Record taint facts in the user-visible result
        if (targetFact is TaintFact taintFact)
        {
            if (!_analysisResults.TryGetValue(targetNode, out var set))
                _analysisResults[targetNode] = set = new HashSet<TaintFact>();
            set.Add(taintFact);
        }

        // Track all facts (including ZeroFact) in the path-edge graph
        var key = (targetNode, targetFact);
        if (!_pathEdges.TryGetValue(key, out var preds))
            _pathEdges[key] = preds = new HashSet<(ICFGNode, IFact)>();

        // Only add the predecessor if it's new to avoid infinite loops on cycles if not using _inQueue
        // (though _inQueue handles the worklist part, path edges can still cycle)
        if (preds.Add((predecessorNode, predecessorFact)))
        {
            // If a new path edge is discovered, potentially add target to worklist
            if (_inQueue.Add(key)) // Check if (targetNode, targetFact) is already in the work queue
            {
                _workQueue.Enqueue(key);
            }
        }
    }

    private ISet<TaintFact> MergeSummary(ICFGEdge ctr, ISet<TaintFact> curr)
    {
        if (_summaryEdges.TryGetValue((ctr.From, (IFact)curr.FirstOrDefault() ?? _problem.ZeroValue), out var summary))
            curr.UnionWith(summary);
        return curr;
    }
}
