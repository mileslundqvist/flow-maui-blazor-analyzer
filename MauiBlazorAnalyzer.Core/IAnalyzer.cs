using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Interface for code analyzers (analysis rules).
/// Each implementation should check for a specific type of issue.
/// </summary>
public interface IAnalyzer
{
    /// <summary>
    /// Unique identifier for this analyzer (e.g., "MBA001").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// User-friendly name of this analyzer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this analyzer checks for.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Default severity for diagnostics produced by this analyzer.
    /// </summary>
    DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>
    /// Analyzes the provided compilation and returns any diagnostics found.
    /// Analyzers might inspect SyntaxTrees, use the SemanticModel, or examine Compilation options/metadata.
    /// </summary>
    /// <param name="project">The project being analyzed.</param>
    /// <param name="compilation">The Roslyn Compilation for the project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of diagnostics found by this analyzer.</returns>
    Task<ImmutableArray<AnalysisDiagnostic>> AnalyzeCompilationAsync(
        Project project,
        Compilation compilation,
        CancellationToken cancellationToken = default);

}
