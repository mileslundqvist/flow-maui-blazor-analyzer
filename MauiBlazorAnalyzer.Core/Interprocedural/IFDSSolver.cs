using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MauiBlazorAnalyzer.Core.Interprocedural;

public class IFDSSolver
{
    private readonly IFDSTabulationProblem _problem;
    private readonly InterproceduralCFG _graph;

    // path‑edges:   ⟨node,fact⟩  -> { ⟨predNode,predFact⟩ }
    private readonly Dictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> _pathEdges = new();

    // summary‑edges: (calleeEntry, inFact) -> { outFact }  (cached after first run)
    private readonly Dictionary<ExplodedGraphNode, HashSet<TaintFact>> _summaryEdges = new();

    // analysis result map: node -> facts that reach it (excluding ZeroFact)
    private readonly Dictionary<ICFGNode, HashSet<TaintFact>> _analysisResults = new();


    // Maps: (calleeEntryNode, entryFact) -> Set of (callSiteNode, callSiteFact) that caused it
    private readonly Dictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> _callContextMap = new();

    private readonly Queue<ExplodedGraphNode> _workQueue = new();
    private readonly HashSet<ExplodedGraphNode> _inQueue = new();

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
                Propagate(new ExplodedGraphNode(node, fact), new ExplodedGraphNode(node, fact));
            }
        }
    }

    private void ProcessWorklist()
    {
        while (_workQueue.Count > 0)
        {
            var currentExplodedNode = _workQueue.Dequeue();
            _inQueue.Remove(currentExplodedNode);
            var (currentNode, currentFact) = currentExplodedNode; // Deconstruct

            foreach (var edge in _graph.GetOutgoingEdges(currentNode))
            {
                var targetNode = edge.To;
                // -- ZeroFact Handling --
                if (currentFact.Equals(_problem.ZeroValue))
                {
                    Propagate(new ExplodedGraphNode(targetNode, currentFact), new ExplodedGraphNode(targetNode, currentFact));
                    continue; // skip user flow‑functions for ⊥
                }

                // -- Non‑zero facts (= TaintFact) --
                var currentTaintFact = (TaintFact)currentFact;

                switch (edge.Type)
                {
                    case EdgeType.Intraprocedural:
                        HandleIntraprocedural(edge, currentTaintFact, currentExplodedNode);
                        break;

                    case EdgeType.Call:
                        HandleCall(edge, currentTaintFact, currentExplodedNode);
                        break;

                    case EdgeType.Return:
                        HandleReturn(edge, currentTaintFact, currentExplodedNode);
                        break;

                    case EdgeType.CallToReturn:
                        HandleCallToReturn(edge, currentTaintFact, currentExplodedNode);
                        break;
                }
            }
        }
    }

    private void Propagate(ExplodedGraphNode targetState, ExplodedGraphNode predecessorState)
    {

        var (targetNode, targetFact) = targetState;

        // Record taint facts in the user-visible result
        if (targetFact is TaintFact taintFact)
        {
            if (!_analysisResults.TryGetValue(targetNode, out var set))
                _analysisResults[targetNode] = set = new HashSet<TaintFact>();
            set.Add(taintFact);
        }

        // Track all facts (including ZeroFact) in the path-edge graph
        var key = targetState;
        if (!_pathEdges.TryGetValue(key, out var preds))
            _pathEdges[key] = preds = new HashSet<ExplodedGraphNode>();

        // Only add the predecessor if it's new to avoid infinite loops on cycles if not using _inQueue
        // (though _inQueue handles the worklist part, path edges can still cycle)
        if (preds.Add(predecessorState))
        {
            // If a new path edge is discovered, potentially add target to worklist
            if (_inQueue.Add(key)) // Check if (targetNode, targetFact) is already in the work queue
            {
                _workQueue.Enqueue(key);
            }
        }
    }


    private void HandleCallToReturn(ICFGEdge edge, TaintFact currentFact, ExplodedGraphNode currentState)
    {
        var normalFlow = _problem.FlowFunctions.GetCallToReturnFlowFunction(edge);
        var successors = normalFlow?.ComputeTargets(currentFact) ?? Enumerable.Empty<TaintFact>();

        var targetNode = edge.To;
        foreach (var successorFact in successors)
        {
            Propagate(new ExplodedGraphNode(targetNode, successorFact), currentState);
        }
    }

    private void HandleIntraprocedural(ICFGEdge edge, TaintFact currentFact, ExplodedGraphNode currentState)
    {
        var normalFlow = _problem.FlowFunctions.GetNormalFlowFunction(edge);
        var successors = normalFlow?.ComputeTargets(currentFact) ?? Enumerable.Empty<TaintFact>();
        var targetNode = edge.To;

        foreach (var successorFact in successors)
        {
            Propagate(new ExplodedGraphNode(targetNode, successorFact), currentState);
        }
    }


    // Call edge handling (non-zero facts)
    private void HandleCall(ICFGEdge callEdge, TaintFact callSiteFact, ExplodedGraphNode callSiteState)
    {
        var callSiteNode = callEdge.From;
        var calleeEntryNode = _graph.GetEntryNode(callEdge.To);

        var callFlowFunction = _problem.FlowFunctions.GetCallFlowFunction(callEdge);
        var entryFacts = callFlowFunction?.ComputeTargets(callSiteFact) ?? new HashSet<TaintFact>();

        foreach (var entryFact in entryFacts)
        {
            var summaryKey = new ExplodedGraphNode(calleeEntryNode, entryFact);
            if (_summaryEdges.TryGetValue(summaryKey, out var cachedExitFacts))
            {
                var callToReturnEdgeTarget = _graph
                    .GetOutgoingEdges(callEdge.From)
                    .FirstOrDefault(e => e.Type == EdgeType.CallToReturn && e.From.Equals(callEdge.From))?.To;

                if (callToReturnEdgeTarget != null)
                {
                    foreach (var exitFact in cachedExitFacts)
                    {
                        var returnFunction = _problem.FlowFunctions.GetReturnFlowFunction(null, callSiteNode);
                        var returnSiteFacts = returnFunction?.ComputeTargets(exitFact) ?? new HashSet<TaintFact>();

                        foreach (var returnFact in returnSiteFacts)
                        {
                            Propagate(new ExplodedGraphNode(callToReturnEdgeTarget, returnFact), callSiteState);
                            continue;
                        }
                    }
                }
                continue;
            }

            // Store call context
            var calleeEntryState = new ExplodedGraphNode(calleeEntryNode, entryFact);

            // Propagate fact into calle entry
            Propagate(calleeEntryState, callSiteState);

            if (!_callContextMap.TryGetValue(calleeEntryState, out var callers))
            {
                callers = new HashSet<ExplodedGraphNode>();
                _callContextMap[calleeEntryState] = callers;
            }

            callers.Add(callSiteState);

              
        }
    }

    // Return edge handling (non-zero facts)
    private void HandleReturn(ICFGEdge returnEdge, TaintFact exitFact, ExplodedGraphNode calleeExitState)
    {
        var calleeExitNode = returnEdge.From;
        var calleeEntryNode = _graph.GetEntryNode(calleeExitNode);
        if (calleeExitNode == null) return;

        var relevantEntryStates = _callContextMap.Keys.Where(k => k.Node.Equals(calleeEntryNode));

        foreach (var entryState in relevantEntryStates)
        {
            var entryFact = entryState.Fact;

            if (_callContextMap.TryGetValue(entryState, out var callingStates))
            {
                foreach (var callSiteState in callingStates)
                {
                    var (callSiteNode, callSiteFact) = callSiteState;

                    // Find the return site node
                    var callToReturnEdge = _graph.GetOutgoingEdges(callSiteNode).FirstOrDefault(e => e.Type == EdgeType.CallToReturn);
                    var returnSiteNode = callToReturnEdge?.To;

                    if (returnSiteNode != null)
                    {
                        // Instantiate ReturnFlow with callsite
                        var returnFlowFunction = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, callSiteNode);

                        // Compute targets
                        var returnSiteFacts = returnFlowFunction.ComputeTargets(exitFact);

                        // Add summary
                        if (entryFact is TaintFact entryTaintFact)
                        {
                            AddSummary(calleeEntryNode, entryTaintFact, returnSiteFacts);
                        }

                        // Propagate results to the return site node
                        foreach (var returnFact in returnSiteFacts)
                        {
                            Propagate(new ExplodedGraphNode(returnSiteNode, returnFact), calleeExitState);
                        }
                    }
                }
            }
        }
    }

    //private IEnumerable<ExplodedGraphNode> FindEntryStatesForExitState(ExplodedGraphNode exitState)
    //{
    //    var (exitNode, exitFact) = exitState;
    //    var entryNode = _graph.GetEntryNode(exitNode);
    //    if (entryNode == null) yield break;

    //    var visited = new HashSet<ExplodedGraphNode>();
    //    var queue = new Queue<ExplodedGraphNode>();

    //    queue.Enqueue(exitState);
    //    visited.Add(exitState);

    //    while (queue.Count > 0)
    //    {
    //        var currentState = queue.Dequeue();
    //        if (currentState.Node.Equals(entryNode))
    //        {
    //            yield return currentState;
    //            continue;
    //        }

    //        if (_pathEdges.TryGetValue(currentState, out var predecessrs))
    //        {
    //            foreach (var predecessorState in predecessrs)
    //            {
    //                if (predecessorState.Node.MethodContext.Equals(exitNode.MethodContext) && visited.Add(predecessorState))
    //                {
    //                    queue.Enqueue(predecessorState);
    //                }
    //            }
    //        }
    //    }
    //}

    private void AddSummary(ICFGNode calleeEntry, IFact entryFact, IEnumerable<TaintFact> outFacts)
    {
        var key = new ExplodedGraphNode(calleeEntry, entryFact);
        if (!_summaryEdges.TryGetValue(key, out var set))
        {
            _summaryEdges[key] = set = new HashSet<TaintFact>();
        }
        set.UnionWith(outFacts);
    }

}
