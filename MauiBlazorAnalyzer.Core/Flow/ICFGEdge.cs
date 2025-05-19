using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;

public enum EdgeType { Intraprocedural, Call, Return, CallToReturn }

public class ICFGEdge
{
    public ICFGNode From { get; }
    public ICFGNode To { get; }
    public EdgeType Type { get; }

    public ICFGEdge(ICFGNode from, ICFGNode to, EdgeType type)
    {
        From = from;
        To = to;
        Type = type;
    }
}
