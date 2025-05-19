using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;
public sealed record ZeroFact : IFact
{
    public static readonly ZeroFact Instance = new ZeroFact();
    private ZeroFact() { }
}
