using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace MauiBlazorAnalyzer.Core.Flow;

public class IFDSSolver
{
    private readonly IFDSTabulationProblem _problem;
    private readonly InterproceduralCFG _graph;
    private readonly ILogger<IFDSSolver> _logger;
    private readonly CancellationToken _cancellationToken;

    // path‑edges:   ⟨node,fact⟩  -> { ⟨predNode,predFact⟩ }
    private readonly ConcurrentDictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> _pathEdges = new();

    // summary‑edges: (calleeEntry, inFact) -> { outFact }  (cached after first run)
    private readonly ConcurrentDictionary<ExplodedGraphNode, HashSet<IFact>> _summaryEdges = new();

    // analysis result map: node -> facts that reach it (excluding ZeroFact)
    private readonly ConcurrentDictionary<ICFGNode, HashSet<TaintFact>> _results = new();

    // Maps: (calleeEntryNode, entryFact) -> Set of (callSiteNode, callSiteFact) that caused it
    private readonly ConcurrentDictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> _callContextMap = new();

    // Work queue with concurrent access
    private readonly ConcurrentQueue<ExplodedGraphNode> _workQueue = new();
    private readonly ConcurrentDictionary<ExplodedGraphNode, bool> _inQueue = new();

    // Cache for frequently accessed edges
    private readonly ConcurrentDictionary<ICFGNode, IEnumerable<ICFGEdge>> _edgeCache = new();

    public IFDSSolver(IFDSTabulationProblem problem, ILogger<IFDSSolver>? logger = null, CancellationToken cancellationToken = default)
    {
        _problem = problem ?? throw new ArgumentNullException(nameof(problem));
        _graph = problem.Graph ?? throw new ArgumentNullException(nameof(problem), "Problem.Graph cannot be null");
        _logger = logger ?? NullLogger<IFDSSolver>.Instance;
        _cancellationToken = cancellationToken;
    }

    public async Task<IFDSAnalysisResult> Solve()
    {
        Initialize();
        await ProcessWorklistAsync().ConfigureAwait(false);
        return new IFDSAnalysisResult(_results.ToDictionary(kv => kv.Key, kv => kv.Value),
                                     _pathEdges.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private void Initialize()
    {
        // Clear all state
        _results.Clear();
        _pathEdges.Clear();
        _summaryEdges.Clear();
        _callContextMap.Clear();
        _edgeCache.Clear();

        // Clear work queue
        while (_workQueue.TryDequeue(out _)) { }
        _inQueue.Clear();

        // Seed the analysis with initial facts
        var initialSeeds = _problem.InitialSeeds;
        foreach (var (node, facts) in initialSeeds)
        {
            foreach (var fact in facts)
            {
                var seedState = new ExplodedGraphNode(node, fact);
                Propagate(seedState, seedState);
            }
        }
    }

    private async Task ProcessWorklistAsync()
    {
        while (TryDequeueWork(out var currentState))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var (currentNode, currentFact) = currentState;

            try
            {
                var edges = await GetCachedOutgoingEdgesAsync(currentNode).ConfigureAwait(false);
                await ProcessEdgesAsync(edges, currentState).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing work item: node={Node}, fact={Fact}",
                    currentNode, currentFact);
            }
        }
    }

    private async Task<IEnumerable<ICFGEdge>> GetCachedOutgoingEdgesAsync(ICFGNode node)
    {
        if (_edgeCache.TryGetValue(node, out var cachedEdges))
        {
            return cachedEdges;
        }

        var edges = await _graph.GetOutgoingEdgesAsync(node, _cancellationToken).ConfigureAwait(false);
        var edgeList = edges.ToList(); // Materialize to avoid multiple enumerations
        _edgeCache.TryAdd(node, edgeList);
        return edgeList;
    }

    private async Task ProcessEdgesAsync(IEnumerable<ICFGEdge> edges, ExplodedGraphNode currentState)
    {
        var tasks = edges.Select(edge => ProcessSingleEdgeAsync(edge, currentState));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ProcessSingleEdgeAsync(ICFGEdge edge, ExplodedGraphNode currentState)
    {
        var (currentNode, currentFact) = currentState;

        try
        {
            switch (edge.Type)
            {
                case EdgeType.Intraprocedural:
                    HandleIntraprocedural(edge, currentFact, currentState);
                    break;

                case EdgeType.Call:
                    await HandleCallAsync(edge, currentFact, currentState).ConfigureAwait(false);
                    break;

                case EdgeType.Return:
                    await HandleReturnAsync(edge, currentFact, currentState).ConfigureAwait(false);
                    break;

                case EdgeType.CallToReturn:
                    HandleCallToReturn(edge, currentFact, currentState);
                    break;

                default:
                    _logger.LogWarning("Unknown edge type: {EdgeType}", edge.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {EdgeType} edge from {From} to {To}",
                edge.Type, edge.From, edge.To);
        }
    }

    private void HandleCallToReturn(ICFGEdge edge, IFact currentFact, ExplodedGraphNode currentState)
    {
        try
        {
            var flowFunction = _problem.FlowFunctions.GetCallToReturnFlowFunction(edge);
            var successors = flowFunction?.ComputeTargets(currentFact) ?? Enumerable.Empty<IFact>();

            foreach (var successorFact in successors)
            {
                Propagate(new ExplodedGraphNode(edge.To, successorFact), currentState);
            }
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleCallToReturn for edge {From} -> {To}", edge.From, edge.To);
            throw;
        }

    }

    private void HandleIntraprocedural(ICFGEdge edge, IFact currentFact, ExplodedGraphNode currentState)
    {
        try
        {
            var flowFunction = _problem.FlowFunctions.GetNormalFlowFunction(edge);
            var successors = flowFunction?.ComputeTargets(currentFact) ?? Enumerable.Empty<IFact>();
            foreach (var successorFact in successors)
            {
                Propagate(new ExplodedGraphNode(edge.To, successorFact), currentState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleIntraprocedural for edge {From} -> {To}", edge.From, edge.To);
            throw;
        }
    }


    private async Task HandleCallAsync(ICFGEdge callEdge, IFact callSiteFact, ExplodedGraphNode callSiteState)
    {
        try
        {
            var callSiteNode = callEdge.From;
            var calleeEntryNode = _graph.GetEntryNode(callEdge.To);

            var flowFunction = _problem.FlowFunctions.GetCallFlowFunction(callEdge);
            var entryFacts = flowFunction?.ComputeTargets(callSiteFact) ?? new HashSet<IFact>();

            foreach (var entryFact in entryFacts)
            {
                var summaryKey = new ExplodedGraphNode(calleeEntryNode, entryFact);

                if (_summaryEdges.TryGetValue(summaryKey, out var cachedExitFacts))
                {
                    await ApplyCachedSummaryAsync(callEdge, callSiteState, cachedExitFacts).ConfigureAwait(false);
                    continue;
                }

                // No cached summary - propagate into callee and record call context
                var calleeEntryState = new ExplodedGraphNode(calleeEntryNode, entryFact);
                Propagate(calleeEntryState, callSiteState);

                // Record call context for later return processing
                var callersSet = _callContextMap.GetOrAdd(calleeEntryState, _ => new HashSet<ExplodedGraphNode>());
                lock (callersSet)
                {
                    callersSet.Add(callSiteState);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleCall for edge {From} -> {To}", callEdge.From, callEdge.To);
            throw;
        }
    }


    private async Task ApplyCachedSummaryAsync(ICFGEdge callEdge, ExplodedGraphNode callSiteState, HashSet<IFact> cachedExitFacts)
    {
        var callToReturnEdge = await FindCallToReturnEdgeAsync(callEdge.From).ConfigureAwait(false);
        if (callToReturnEdge?.To == null) return;

        var returnFlowFunction = _problem.FlowFunctions.GetReturnFlowFunction(null, callEdge.From);

        foreach (var exitFact in cachedExitFacts)
        {
            var returnSiteFacts = returnFlowFunction?.ComputeTargets(exitFact) ?? Enumerable.Empty<IFact>();

            foreach (var returnFact in returnSiteFacts)
            {
                Propagate(new ExplodedGraphNode(callToReturnEdge.To, returnFact), callSiteState);
            }
        }
    }

    private async Task<ICFGEdge?> FindCallToReturnEdgeAsync(ICFGNode callSiteNode)
    {
        var edges = await GetCachedOutgoingEdgesAsync(callSiteNode).ConfigureAwait(false);
        return edges.FirstOrDefault(e => e.Type == EdgeType.CallToReturn);
    }


    private async Task HandleReturnAsync(ICFGEdge returnEdge, IFact exitFact, ExplodedGraphNode calleeExitState)
    {
        try
        {
            var calleeExitNode = returnEdge.From;
            var calleeEntryNode = _graph.GetEntryNode(calleeExitNode);

            // Find all relevant entry states for this callee
            var relevantEntryStates = _callContextMap.Keys
                .Where(k => k.Node.Equals(calleeEntryNode))
                .ToList();

            var processingTasks = relevantEntryStates.Select(entryState =>
                ProcessReturnForEntryStateAsync(entryState, returnEdge, exitFact, calleeExitState));

            await Task.WhenAll(processingTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleReturn for edge {From} -> {To}", returnEdge.From, returnEdge.To);
            throw;
        }
    }

    private async Task ProcessReturnForEntryStateAsync(
        ExplodedGraphNode entryState,
        ICFGEdge returnEdge,
        IFact exitFact,
        ExplodedGraphNode calleeExitState)
    {
        if (!_callContextMap.TryGetValue(entryState, out var callingStates)) return;

        List<ExplodedGraphNode> callers;
        lock (callingStates)
        {
            callers = callingStates.ToList(); // Create snapshot to avoid holding lock
        }

        foreach (var callSiteState in callers)
        {
            var (callSiteNode, _) = callSiteState;

            // Find the return site node
            var callToReturnEdge = await FindCallToReturnEdgeAsync(callSiteNode).ConfigureAwait(false);
            var returnSiteNode = callToReturnEdge?.To;

            if (returnSiteNode == null) continue;

            // Compute return facts
            var returnFlowFunction = _problem.FlowFunctions.GetReturnFlowFunction(returnEdge, callSiteNode);
            var returnSiteFacts = returnFlowFunction?.ComputeTargets(exitFact) ?? Enumerable.Empty<IFact>();
            var materializedFacts = returnSiteFacts.ToList();

            // Add summary for caching
            AddSummary(entryState.Node, entryState.Fact, materializedFacts);

            // Propagate results to the return site
            foreach (var returnFact in materializedFacts)
            {
                Propagate(new ExplodedGraphNode(returnSiteNode, returnFact), calleeExitState);
            }
        }
    }

    private void Propagate(ExplodedGraphNode targetState, ExplodedGraphNode predecessorState)
    {

        var (targetNode, targetFact) = targetState;

        // Record taint facts in the user-visible result
        if (targetFact is TaintFact taintFact)
        {
            var resultSet = _results.GetOrAdd(targetNode, _ => new HashSet<TaintFact>());
            lock (resultSet)
            {
                resultSet.Add(taintFact);
            }
        }

        // Track all facts (including ZeroFact) in the path-edge graph
        var predessorSet = _pathEdges.GetOrAdd(targetState, _ => new HashSet<ExplodedGraphNode>());
        bool isNewEdge;

        lock (predessorSet)
        {
            isNewEdge = predessorSet.Add(predecessorState);
        }

        // Only add to worklist if this is a new path edge
        if (isNewEdge)
        {
            if (_inQueue.TryAdd(targetState, true))
            {
                _workQueue.Enqueue(targetState);
            }
        }
    }

    private bool TryDequeueWork(out ExplodedGraphNode state)
    {
        if (_workQueue.TryDequeue(out state))
        {
            _inQueue.TryRemove(state, out _);
            return true;
        }
        state = default;
        return false;
    }

    private void AddSummary(ICFGNode calleeEntry, IFact entryFact, IEnumerable<IFact> outFacts)
    {
        var key = new ExplodedGraphNode(calleeEntry, entryFact);
        var summarySet = _summaryEdges.GetOrAdd(key, _ => new HashSet<IFact>());

        lock (summarySet)
        {
            summarySet.UnionWith(outFacts);
        }
    }

}
