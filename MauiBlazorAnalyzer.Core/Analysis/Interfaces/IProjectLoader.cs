using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Analysis.Interfaces;
public interface IProjectLoader
{
    Task<ImmutableArray<(Project Project, Compilation? Compilation)>> LoadProjectsAndCompilationsAsync(
        string inputPath,
        CancellationToken cancellationToken = default);
}
