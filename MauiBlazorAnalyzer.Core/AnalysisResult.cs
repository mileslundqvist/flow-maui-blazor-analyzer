using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Results of an analysis run
/// </summary>
public class AnalysisResult
{
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    public AnalysisStatistics Statistics { get; }

    public AnalysisResult(
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        AnalysisStatistics statistics)
    {
        Diagnostics = diagnostics.IsDefault ?
            ImmutableArray<AnalysisDiagnostic>.Empty :
            diagnostics;
        Statistics = statistics ?? new AnalysisStatistics();
    }
}
