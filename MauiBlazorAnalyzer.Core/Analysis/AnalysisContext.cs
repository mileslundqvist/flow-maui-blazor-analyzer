using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Analysis;
public class AnalysisContext
{
    public Solution Solution { get; }
    public Compilation RootCompilation { get; }

}
