namespace MauiBlazorAnalyzer.Core.Interprocedural;

public class IFDSSolver
{
    private readonly IFDSTabulationProblem _problem;
    private readonly InterproceduralCFG _graph;

    // path‑edges:   ⟨node,fact⟩  -> { ⟨predNode,predFact⟩ }
    private readonly Dictionary<(ICFGNode node, TaintFact fact), HashSet<(ICFGNode predNode, TaintFact predFact)>> _pathEdges = new();

    // summary‑edges: (calleeEntry, inFact) -> { outFact }  (cached after first run)
    private readonly Dictionary<(ICFGNode entry, TaintFact inFact), HashSet<TaintFact>> _summaryEdges = new();

    // analysis result map: node -> facts that reach it (excluding ZeroFact)
    private readonly Dictionary<ICFGNode, HashSet<TaintFact>> _analysisResults = new();

    private readonly Queue<(ICFGNode node, TaintFact fact)> _workQueue = new();
    private readonly HashSet<(ICFGNode node, TaintFact fact)> _inQueue = new();

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
                Propagate(kvp.Key, fact, kvp.Key, fact);
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
                if (fact.Equals(_problem.ZeroValue) && edge.Type is not (EdgeType.Call or EdgeType.CallToReturn or EdgeType.Return))
                {
                    continue;
                }

                ISet<TaintFact> successorFacts = edge.Type switch
                {
                    EdgeType.Intraprocedural => _problem.FlowFunctions.GetNormalFlowFunction(edge, fact)?.ComputeTargets(fact) ?? new HashSet<TaintFact>(),
                    EdgeType.Call => HandleCallEdge(edge, fact),
                    EdgeType.Return => HandleReturnFlow(edge, fact),
                    EdgeType.CallToReturn => _problem.FlowFunctions.GetCallToReturnFlowFunction(edge, fact)?.ComputeTargets(fact) ?? new HashSet<TaintFact>(),
                    _ => new HashSet<TaintFact>()
                };

                foreach (var succesorFact in successorFacts)
                {
                    Propagate(edge.To, succesorFact, node, fact);
                }
            }
        }
    }

    // Call edge handling
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

        var set = new HashSet<TaintFact>();
        _summaryEdges[summaryKey] = set;
        return set;

    }

    private ISet<TaintFact> HandleReturnFlow(ICFGEdge returnEdge, TaintFact exitFact)
    {
        var calleeEntry = _graph.GetEntryNode(returnEdge.From);

        var incomingPairs = _pathEdges.Where(kvp => kvp.Key.node == returnEdge.From && kvp.Key.fact.Equals(exitFact)).SelectMany(kvp => kvp.Value);

        var results = new HashSet<TaintFact>();

        foreach ( var (callSiteNode, callSiteFact) in incomingPairs)
        {
            var callEdge = _graph.Get
        }
    }

    // Placeholder for the complex logic of finding relevant call site facts
    private IEnumerable<(ICFGNode callSiteNode, TaintFact callSiteFact)> FindRelevantCallSiteFacts(ICFGNode exitNode, TaintFact exitFact)
    {
        // Implementation depends on how you track paths/contexts.
        // Needs to query _pathEdges or similar structures built during propagation.
        yield break; // Replace with actual implementation
    }

    private void Propagate(ICFGNode targetNode, TaintFact targetFact, ICFGNode predecessorNode, TaintFact predecessorFact)
    {
        if (!_analysisResults.TryGetValue(targetNode, out var factsAtTarget))
        {
            factsAtTarget = new HashSet<TaintFact>();
            _analysisResults.Add(targetNode, factsAtTarget);
        }

        if (factsAtTarget.Add(targetFact))
        {
            _workQueue.Enqueue((targetNode, targetFact));

            var key = (targetNode, targetFact);
            if (!_pathEdges.TryGetValue(key, out var preds))
            {
                preds = new HashSet<(ICFGNode, TaintFact)>();
                _pathEdges.Add(key, preds);
            }
            preds.Add((predecessorNode, predecessorFact));
        }
    }
}
