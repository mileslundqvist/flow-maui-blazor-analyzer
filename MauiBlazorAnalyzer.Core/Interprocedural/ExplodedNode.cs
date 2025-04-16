namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class ExplodedNode<N, D>
{
    public N Node { get; }
    public D Fact { get; }

    public ExplodedNode(N node, D fact)
    {
        Node = node;
        Fact = fact;
    }

}
