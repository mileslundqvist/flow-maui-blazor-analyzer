using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Analysis;

/// <summary>
/// Configuration for an analysis run
/// </summary>
public class AnalysisOptions
{
    /// <summary>
    /// Path to the project or solution file (.csproj or .sln) to analyze
    /// </summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>
    /// Output format for results (e.g., "console", "json", "sarif")
    /// </summary>
    public string OutputFormat { get; set; } = "console";

    /// <summary>
    /// Output file path for results (if applicable, null for console)
    /// </summary>
    public string? OutputPath { get; set; }


    /// <summary>
    /// Minimum severity level to report
    /// </summary>
    public DiagnosticSeverity MinimumSeverity { get; set; } = DiagnosticSeverity.Warning;
}
