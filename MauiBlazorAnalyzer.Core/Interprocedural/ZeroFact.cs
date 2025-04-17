namespace MauiBlazorAnalyzer.Core.Interprocedural;
public sealed record ZeroFact : IFact
{
    public static readonly ZeroFact Instance = new ZeroFact();
    private ZeroFact() { }
}
