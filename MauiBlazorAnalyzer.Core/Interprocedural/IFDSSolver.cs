namespace MauiBlazorAnalyzer.Core.Interprocedural;

public class IFDSSolver
{
    private readonly IFDSTabulationProblem _problem;
    private readonly InterproceduralCFG _graph;
    private readonly Dictionary<ICFGNode, HashSet<TaintFact>> _analysisResults = new();
    private readonly Queue<(ICFGNode node, TaintFact fact)> _worklist = new();
    private readonly Dictionary<(ICFGNode node, TaintFact fact), HashSet<(ICFGNode predNode, TaintFact predFact)>> _pathEdges = new();

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

    private void Initialize()
    {
        _analysisResults.Clear();
        _worklist.Clear();
        _pathEdges.Clear();

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
        while (_worklist.Count > 0)
        {
            var (node, fact) = _worklist.Dequeue();

            foreach (var edge in _graph.GetOutgoingEdges(node))
            {
                ISet<TaintFact> results = new HashSet<TaintFact>();
                if (edge.Type == EdgeType.Return)
                {
                    results = HandleReturnFlow(edge, fact);
                }
                else
                {
                    IFlowFunction? flowFunction = null;
                    switch (edge.Type)
                    {
                        case EdgeType.Intraprocedural:
                            flowFunction = _problem.FlowFunctions.GetNormalFlowFunction(edge, fact);
                            break;
                        case EdgeType.Call:
                            flowFunction = _problem.FlowFunctions.GetCallFlowFunction(edge, fact);
                            break;
                        case EdgeType.CallToReturn:
                            flowFunction = _problem.FlowFunctions.GetCallToReturnFlowFunction(edge, fact);
                            break;
                    }

                    if (flowFunction != null)
                    {
                        results = flowFunction.ComputeTargets(fact);
                    }
                }

                foreach (var resultFact in results)
                {
                    if (resultFact is TaintFact resultAsTFact)
                    {
                        Propagate(edge.To, resultAsTFact, node, fact);
                    }
                }
            }
        }
    }

    private ISet<TaintFact> HandleReturnFlow(ICFGEdge returnEdge, TaintFact exitFact) // Takes specific exit fact type
    {
        var returnSiteNode = returnEdge.To;
        var exitNode = returnEdge.From;
        HashSet<TaintFact> propagatedFacts = new HashSet<TaintFact>();

        // --- Crucial: Find relevant call site facts ---
        // You need to trace back path edges or use summaries to find pairs of
        // (callSiteNode, callSiteFact) that could have led to exitFact at exitNode.

        // Example placeholder loop (replace with actual logic):
        IEnumerable<(ICFGNode callSiteNode, TaintFact callSiteFactAsTFact)> relevantCallSiteData = FindRelevantCallSiteFacts(exitNode, exitFact);

        foreach (var (callSiteNode, callSiteFactAsTFact) in relevantCallSiteData)
        {
            if (callSiteFactAsTFact is TaintFact callSiteFact) // Ensure type compatibility
            {
                // Get the specific return flow function for this combination
                IFlowFunction returnFn = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, exitFact, callSiteFact);

                // Compute targets based on the exit fact
                propagatedFacts.UnionWith(returnFn.ComputeTargets(exitFact));
            }
        }

        return propagatedFacts;
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
            _worklist.Enqueue((targetNode, targetFact));

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
