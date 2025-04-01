namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Interface for code analyzers
/// </summary>
public interface IAnalyzer
{
    /// <summary>
    /// Unique identifier for this analyzer
    /// </summary>
    string Id { get; }

    /// <summary>
    /// User-friendly name of this analyzer
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this analyzer checks for
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Analyzes the provided context and returns results
    /// </summary>
    Task<AnalysisResult> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default);
}
