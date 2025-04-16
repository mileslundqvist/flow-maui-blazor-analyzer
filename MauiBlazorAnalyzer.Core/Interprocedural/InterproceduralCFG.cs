using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class InterproceduralCFG : IInterproceduralCFG<ICFGNode, IMethodSymbol>
{
    private readonly List<ICFGEdge> _edges = new List<ICFGEdge>();
    private readonly Dictionary<ICFGNode, List<ICFGEdge>> _outgoingEdges = new Dictionary<ICFGNode, List<ICFGEdge>>();

    public void AddEdge(ICFGNode from, ICFGNode to, EdgeType type)
    {
        ICFGEdge edge = new ICFGEdge(from, to, type);
        _edges.Add(edge);

        // Add to outgoing edges of 'from'
        if (!_outgoingEdges.TryGetValue(from, out var outList))
        {
            outList = new List<ICFGEdge>();
            _outgoingEdges[from] = outList;
        }
        outList.Add(edge);;
    }

    public IEnumerable<ICFGEdge> GetOutgoingEdges(ICFGNode node, EdgeType? typeFilter = null)
    {
        if (_outgoingEdges.TryGetValue(node, out var edges))
        {
            if (typeFilter.HasValue)
                return edges.Where(e => e.Type == typeFilter.Value);
            return edges;
        }
        return Enumerable.Empty<ICFGEdge>();
    }

    public IEnumerable<ICFGEdge> GetAllEdges()
    {
        return _edges;
    }

    public IEnumerable<IMethodSymbol> GetCalleesAtCallSite(ICFGNode callSite)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ICFGNode> GetCallSites(IMethodSymbol method)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ICFGNode> GetExitNodes(IMethodSymbol callee)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ICFGNode> GetReturnSites(ICFGNode callSite)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<ICFGNode> GetSuccessors(ICFGNode node)
    {
        throw new NotImplementedException();
    }
}
