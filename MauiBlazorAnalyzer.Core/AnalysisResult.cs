using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Results of an analysis run
/// </summary>
public class AnalysisResult
{
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    public AnalysisStatistics Statistics { get; }
    public bool Succeeded { get; }
    public string? ErrorMessage { get; }

    // Constructor for successful result
    public AnalysisResult(ImmutableArray<AnalysisDiagnostic> diagnostics, AnalysisStatistics statistics)
    {
        Diagnostics = diagnostics.IsDefault ? ImmutableArray<AnalysisDiagnostic>.Empty : diagnostics;
        Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        Succeeded = true;
        ErrorMessage = null;
    }

    // Constructor for failed result
    public AnalysisResult(string errorMessage, AnalysisStatistics statistics)
    {
        Diagnostics = ImmutableArray<AnalysisDiagnostic>.Empty;
        Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        Succeeded = false;
        ErrorMessage = errorMessage;
    }
}
