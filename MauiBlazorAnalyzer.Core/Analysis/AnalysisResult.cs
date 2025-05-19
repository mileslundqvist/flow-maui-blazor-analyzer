using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Analysis;

/// <summary>
/// Results of an analysis run
/// </summary>
public class AnalysisResult
{
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    public AnalysisStatistics Statistics { get; }
    public bool Succeeded { get; }
    public string? ErrorMessage { get; }

    // Private constructor enforces use of factory methods
    private AnalysisResult(ImmutableArray<AnalysisDiagnostic> diagnostics, AnalysisStatistics statistics, bool succeeded, string? errorMessage)
    {
        Diagnostics = diagnostics.IsDefault ? ImmutableArray<AnalysisDiagnostic>.Empty : diagnostics;
        Statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
        Succeeded = succeeded;
        ErrorMessage = Succeeded ? null : errorMessage;
    }
    /// <summary>
    /// Creates a successful analysis result.
    /// </summary>
    public static AnalysisResult CreateSuccess(ImmutableArray<AnalysisDiagnostic> diagnostics, AnalysisStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        // Ensure diagnostics is not default (uninitialized)
        var validDiagnostics = diagnostics.IsDefault ? ImmutableArray<AnalysisDiagnostic>.Empty : diagnostics;
        return new AnalysisResult(validDiagnostics, statistics, succeeded: true, errorMessage: null);
    }

    /// <summary>
    /// Creates a failed analysis result.
    /// </summary>
    public static AnalysisResult CreateFailure(string errorMessage, AnalysisStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage); // Ensure error message is provided
        return new AnalysisResult(ImmutableArray<AnalysisDiagnostic>.Empty, statistics, succeeded: false, errorMessage: errorMessage);
    }

}
