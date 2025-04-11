using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Analysis;
public record ProjectAnalysisResult(
        ImmutableArray<AnalysisDiagnostic> Diagnostics,
        ProjectAnalysisStatistics Statistics
    );
