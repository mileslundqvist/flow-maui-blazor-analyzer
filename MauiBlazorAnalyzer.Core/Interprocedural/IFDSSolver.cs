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
        foreach (var kvp in initial)
        {
            foreach (var fact in kvp.Value)
            {
                Propagate(kvp.Key, fact, kvp.Key, fact, enqueueZero: true);
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
                    switch (edge.Type)
                    {
                        case EdgeType.Call:
                            // propagate ⊥ to callee entry
                            var calleeEntry = _graph.GetEntryNode(edge.To);
                            Propagate(calleeEntry, fact, node, fact, enqueueZero: true);
                            break;
                        case EdgeType.CallToReturn:
                        case EdgeType.Return:
                            // propagate ⊥ straight across
                            Propagate(edge.To, fact, node, fact, enqueueZero: true);
                            break;
                            // Intraprocedural edges do *not* carry ⊥.
                    }
                    continue; // skip user flow‑functions for ⊥
                }

                // —— Non‑zero facts (= TaintFact) --

                var tFact = (TaintFact)fact;
                ISet<TaintFact> successorFacts = edge.Type switch
                {
                    EdgeType.Intraprocedural => _problem.FlowFunctions.GetNormalFlowFunction(edge, tFact)?.ComputeTargets(tFact) ?? new HashSet<TaintFact>(),
                    EdgeType.Call => HandleCallEdge(edge, tFact),
                    EdgeType.Return => HandleReturnFlow(edge, tFact),
                    EdgeType.CallToReturn => _problem.FlowFunctions.GetCallToReturnFlowFunction(edge, tFact)?.ComputeTargets(tFact) ?? new HashSet<TaintFact>(),
                    _ => new HashSet<TaintFact>()
                };

                foreach (var succesorFact in successorFacts)
                {
                    Propagate(edge.To, succesorFact, node, tFact);
                }
            }
        }
    }

    // Call edge handling (non-zero facts)
    private ISet<TaintFact> HandleCallEdge(ICFGEdge callEdge, TaintFact inFact)
    {
        var summaryKey = (callEdge.To, inFact);

        if (_summaryEdges.TryGetValue(summaryKey, out var cached))
        {
            return cached;
        }

        var callFlowFunction = _problem.FlowFunctions.GetCallFlowFunction(callEdge, inFact);
        var seeds = callFlowFunction?.ComputeTargets(inFact) ?? new HashSet<TaintFact>();

        foreach (var seedFact in seeds)
        {
            Propagate(callEdge.To, seedFact, predecessorNode: callEdge.From, predecessorFact: inFact);
        }

        var empty = new HashSet<TaintFact>();
        _summaryEdges[summaryKey] = empty;
        return empty;

    }

    // Return edge handling (non-zero facts)
    private ISet<TaintFact> HandleReturnFlow(ICFGEdge returnEdge, TaintFact exitFact)
    {
        var calleeEntry = _graph.GetEntryNode(returnEdge.From);

        var incomingPairs = _pathEdges.Where(kvp => kvp.Key.node == returnEdge.From && kvp.Key.fact.Equals(exitFact)).SelectMany(kvp => kvp.Value);

        var results = new HashSet<TaintFact>();

        foreach (var (callSite, callFactRaw) in incomingPairs)
        {
            if (callFactRaw is not TaintFact callFact) continue; // Ignore ZeroFact predecessors
            var callEdge = _graph.TryGetCallEdge(callSite, calleeEntry);
            if (callEdge is null) continue;

            var returnFunction = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, exitFact, callFact);
            var targets = returnFunction?.ComputeTargets(exitFact) ?? new HashSet<TaintFact>();

            var key = (calleeEntry, callFact);
            if (!_summaryEdges.TryGetValue(key, out var set))
            {
                _summaryEdges[key] = set = new HashSet<TaintFact>();
            }
            set.UnionWith(targets);
            results.UnionWith(targets);
        }
        return results;
    }

    // Placeholder for the complex logic of finding relevant call site facts
    private IEnumerable<(ICFGNode callSiteNode, TaintFact callSiteFact)> FindRelevantCallSiteFacts(ICFGNode exitNode, TaintFact exitFact)
    {
        // Implementation depends on how you track paths/contexts.
        // Needs to query _pathEdges or similar structures built during propagation.
        yield break; // Replace with actual implementation
    }

    private void Propagate(ICFGNode targetNode, IFact targetFact, ICFGNode predecessorNode, IFact predecessorFact, bool enqueueZero = false)
    {

        // Record taint facts in the user-visible result
        if (targetFact is TaintFact taintFact)
        {
            if (!_analysisResults.TryGetValue(targetNode, out var set))
                _analysisResults[targetNode] = set = new HashSet<TaintFact>();
            set.Add(taintFact);
        }

        // Track all facts in the path-edge graph
        var key = (targetNode, targetFact);
        if (!_pathEdges.TryGetValue(key, out var preds))
            _pathEdges[key] = preds = new HashSet<(ICFGNode, IFact)>();
        preds.Add((predecessorNode, predecessorFact));

        // Work-set management
        if (enqueueZero || targetFact is TaintFact && _inQueue.Add(key))
            _workQueue.Enqueue(key);

    }
}
