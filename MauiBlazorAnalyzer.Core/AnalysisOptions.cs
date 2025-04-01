using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Configuration for analysis
/// </summary>
public class AnalysisOptions
{
    /// <summary>
    /// Path to the project or solution to analyze
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Output format for results (console, json, etc.)
    /// </summary>
    public string OutputFormat { get; set; } = "console";

    /// <summary>
    /// Output path for results (if applicable)
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// Analyzer IDs to include (empty means all)
    /// </summary>
    public ImmutableArray<string> IncludeAnalyzers { get; set; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Analyzer IDs to exclude
    /// </summary>
    public ImmutableArray<string> ExcludeAnalyzers { get; set; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Minimum severity level to report
    /// </summary>
    public DiagnosticSeverity MinimumSeverity { get; set; } = DiagnosticSeverity.Warning;
}
