namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class IFDSAnalysisResult
{
    private readonly Dictionary<ICFGNode, HashSet<TaintFact>> _results;
    private readonly Dictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> _pathEdges;

    public IFDSAnalysisResult(Dictionary<ICFGNode, HashSet<TaintFact>> results, Dictionary<ExplodedGraphNode, HashSet<ExplodedGraphNode>> pathEdges)
    {
        _results = results;
        _pathEdges = pathEdges;
    }

    public IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> Results =>
       _results.ToDictionary(kvp => kvp.Key, kvp => (ISet<TaintFact>)kvp.Value);

    public IReadOnlyDictionary<ExplodedGraphNode, ISet<ExplodedGraphNode>> PathEdges =>
        _pathEdges.ToDictionary(kvp => kvp.Key, kvp => (ISet<ExplodedGraphNode>)kvp.Value);

    public IEnumerable<ExplodedGraphNode> GetPathPredecessors(ExplodedGraphNode targetState)
    {
        if (_pathEdges.TryGetValue(targetState, out var preds))
        {
            return preds;
        }
        return Enumerable.Empty<ExplodedGraphNode>();
    }
}
