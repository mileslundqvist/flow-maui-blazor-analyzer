using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Analysis.Interfaces;
public interface IProjectLoader
{
    Task<ImmutableArray<(Project Project, Compilation? Compilation)>> LoadProjectsAndCompilationsAsync(
        string inputPath,
        CancellationToken cancellationToken = default);
}
