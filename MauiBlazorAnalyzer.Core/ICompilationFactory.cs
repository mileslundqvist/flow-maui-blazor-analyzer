using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Factory for creating compilation instances
/// </summary>
public interface ICompilationFactory
{
    /// <summary>
    /// Creates a compilation with analyzers from a project
    /// </summary>
    Task<CompilationWithAnalyzers> CreateFromProjectAsync(
        Project project,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        CancellationToken cancellationToken = default);
}
