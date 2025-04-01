namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Interface for reporting analysis results
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Reports analysis results
    /// </summary>
    Task ReportAsync(AnalysisResult result, AnalysisOptions options, CancellationToken cancellationToken = default);
}
