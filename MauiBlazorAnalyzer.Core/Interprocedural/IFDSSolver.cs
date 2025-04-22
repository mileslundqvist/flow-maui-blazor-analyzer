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
                ISet<TaintFact> successorFacts = new HashSet<TaintFact>(); // Initialize empty set

                switch (edge.Type)
                {
                    case EdgeType.Intraprocedural:
                        var normalFlow = _problem.FlowFunctions.GetNormalFlowFunction(edge);
                        successorFacts.UnionWith(normalFlow?.ComputeTargets(currentTaintFact) ?? new HashSet<TaintFact>());
                        break;

                    case EdgeType.Call:
                        successorFacts.UnionWith(HandleCall(edge, currentTaintFact, currentExplodedNode));
                        break;

                    case EdgeType.Return:
                        successorFacts.UnionWith(HandleReturn(edge, currentTaintFact));
                        break;

                    case EdgeType.CallToReturn:
                        var ctrFlow = _problem.FlowFunctions.GetCallToReturnFlowFunction(edge);
                        successorFacts.UnionWith(ctrFlow?.ComputeTargets(currentTaintFact) ?? new HashSet<TaintFact>());
                        break;
                }

                // Propagate the calculated successor facts to the target node (edge.To)
                foreach (var succesorFact in successorFacts)
                {
                    Propagate(new ExplodedGraphNode(targetNode, succesorFact), new ExplodedGraphNode(targetNode, succesorFact));
                }
            }
        }
    }

    // Call edge handling (non-zero facts)
    private ISet<TaintFact> HandleCall(ICFGEdge callEdge, TaintFact callSiteFact, ExplodedGraphNode callSiteState)
    {
        var callSiteNode = callEdge.From;
        var calleeEntryNode = _graph.GetEntryNode(callEdge.To);

        var callFlowFunction = _problem.FlowFunctions.GetCallFlowFunction(callEdge);
        var entryFacts = callFlowFunction?.ComputeTargets(callSiteFact) ?? new HashSet<TaintFact>();

        foreach (var entryFact in entryFacts)
        {
            // Store call context
            var calleeEntryState = new ExplodedGraphNode(calleeEntryNode, entryFact);


            if (!_callContextMap.TryGetValue(calleeEntryState, out var callers))
            {
                callers = new HashSet<ExplodedGraphNode>();
                _callContextMap[calleeEntryState] = callers;
            }

            callers.Add(callSiteState);

        }

        //TODO: Summary manage, propagate results directly to the return site
        //if (_summaryEdges.TryGetValue((calleeEntry, inFactAtCallsite), out var cachedExitFacts))
        //{
            
        //    var callToReturnEdgeTarget = _graph
        //        .GetOutgoingEdges(callEdge.From)
        //        .FirstOrDefault(e => e.Type == EdgeType.CallToReturn && e.From.Equals(callEdge.From))?.To;

        //    if (callToReturnEdgeTarget != null)
        //    {
        //        // Handle summary in a correct way
        //        //foreach (var exitFact in cachedExitFacts)
        //        //{
        //        //    var returnFunction = _problem.FlowFunctions.GetReturnFlowFunction(null, null); // Pass null for edge, ReturnFlow might not need it
        //        //    var returnSiteFacts = returnFunction?.ComputeTargets(exitFact) ?? new HashSet<TaintFact>();

        //        //    foreach (var returnFact in returnSiteFacts)
        //        //    {
        //        //        Propagate(callToReturnEdgeTarget, returnFact, callEdge.From, inFactAtCallsite);
        //        //    }
        //        //}
        //    }
        //}

        return entryFacts;

    }

    // Return edge handling (non-zero facts)
    private ISet<TaintFact> HandleReturn(ICFGEdge returnEdge, TaintFact exitFact)
    {
        var outSet = new HashSet<TaintFact>();
        var exitNode = returnEdge.From;
        var calleeEntryNode = _graph.GetEntryNode(exitNode);

        foreach (var entryState in _callContextMap.Keys.Where(k => k.Node.Equals(calleeEntryNode)))
        {
            // *** TODO: Check if entryState *can* lead to exitState ***
            // This is the crucial link requiring intra-procedural summary/path info.
            // bool pathExists = CheckPathExists(entryState, exitState); // Placeholder for summary lookup
            // if (!pathExists) continue;

            if (_callContextMap.TryGetValue(entryState, out var callingStates))
            {
                foreach (var callSiteState in callingStates)
                {
                    var callSiteNode = callSiteState.Node;

                    // Find the return site node
                    var returnSiteNode = _graph.GetOutgoingEdges(callSiteNode).FirstOrDefault(e => e.Type == EdgeType.CallToReturn);

                    if (returnSiteNode != null)
                    {
                        // Instantiate ReturnFlow with callsite
                        var returnFlowFunction = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, callSiteNode);

                        // Compute targets
                        var returnSiteFacts = returnFlowFunction.ComputeTargets(exitFact);

                        outSet.UnionWith(returnSiteFacts);
                    }
                }
            }
        }

        return outSet;
    }

    private void Propagate(ExplodedGraphNode targetState, ExplodedGraphNode predecessorState)
    {

        var (targetNode, targetFact) = targetState;
        var (predecessorNode, predecessorFact) = predecessorState;
        // Record taint facts in the user-visible result
        if (targetFact is TaintFact taintFact)
        {
            if (!_analysisResults.TryGetValue(targetNode, out var set))
                _analysisResults[targetNode] = set = new HashSet<TaintFact>();
            set.Add(taintFact);
        }

        // Track all facts (including ZeroFact) in the path-edge graph
        var key = new ExplodedGraphNode(targetNode, targetFact);
        if (!_pathEdges.TryGetValue(key, out var preds))
            _pathEdges[key] = preds = new HashSet<ExplodedGraphNode>();

        // Only add the predecessor if it's new to avoid infinite loops on cycles if not using _inQueue
        // (though _inQueue handles the worklist part, path edges can still cycle)
        if (preds.Add(new ExplodedGraphNode(predecessorNode, predecessorFact)))
        {
            // If a new path edge is discovered, potentially add target to worklist
            if (_inQueue.Add(key)) // Check if (targetNode, targetFact) is already in the work queue
            {
                _workQueue.Enqueue(key);
            }
        }
    }

}
