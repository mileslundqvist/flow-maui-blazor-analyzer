using MauiBlazorAnalyzer.Core.Analysis;
using MauiBlazorAnalyzer.Core.Analysis.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using AnalysisResult = MauiBlazorAnalyzer.Core.Analysis.AnalysisResult;

namespace MauiBlazorAnalyzer.Infrastructure;
public class ConsoleReporter : IReporter
{
    private readonly ILogger<ConsoleReporter> _logger;

    public ConsoleReporter(ILogger<ConsoleReporter> logger)
    {
        _logger = logger;
    }

    public Task ReportAsync(AnalysisResult result, AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("\n--- Analysis Report ---");

        if (!result.Succeeded)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Analysis Failed: {result.ErrorMessage}");
            Console.ResetColor();
        }

        PrintAnalysisReport(result);

        if (result.Diagnostics.Any())
        {
            PrintDiagnostics(result, cancellationToken);
        }
        else if (result.Succeeded)
        {
            Console.WriteLine("\nNo issues found matching the criteria.");
        }

        Console.WriteLine("\n--- End Report ---");
        _logger.LogInformation("Console report generated.");
        return Task.CompletedTask;
    }

    private static void PrintDiagnostics(AnalysisResult result, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n[Diagnostics]");
        // Group by file for better readability
        var groupedDiagnostics = result.Diagnostics
                                    .OrderBy(d => d.FilePath ?? string.Empty)
                                    .ThenBy(d => d.Location.StartLinePosition.Line)
                                    .ThenBy(d => d.Location.StartLinePosition.Character)
                                    .GroupBy(d => d.FilePath ?? "General");

        foreach (var group in groupedDiagnostics)
        {
            Console.WriteLine($"\n  File: {group.Key}");
            foreach (var diag in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string severityString = diag.Severity.ToString().ToUpperInvariant();
                ConsoleColor color = diag.Severity switch
                {
                    DiagnosticSeverity.Error => ConsoleColor.Red,
                    DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                    DiagnosticSeverity.Info => ConsoleColor.Cyan,
                    _ => Console.ForegroundColor
                };

                Console.Write($"    {diag.Location.Path}({diag.Location.StartLinePosition.Line + 1},{diag.Location.StartLinePosition.Character + 1}): ");
                Console.ForegroundColor = color;
                Console.Write($"{severityString} {diag.Id}");
                Console.ResetColor();
                Console.Write($": {diag.Title} - {diag.Message}");
                if (!string.IsNullOrEmpty(diag.HelpLink)) Console.Write($" [{diag.HelpLink}]");
                Console.WriteLine();
            }
        }
    }

    private static void PrintAnalysisReport(Core.Analysis.AnalysisResult result)
    {
        Console.WriteLine("\n[Statistics]");
        Console.WriteLine($"- Analysis Duration: {result.Statistics.AnalysisDuration}");
        Console.WriteLine($"- Total Files Analyzed: {result.Statistics.TotalFilesAnalyzed}");
        Console.WriteLine($"- C# Files Analyzed: {result.Statistics.CSharpFilesAnalyzed}");
        Console.WriteLine($"- Razor Files Analyzed: {result.Statistics.RazorFilesAnalyzed}");
        Console.WriteLine($"- Diagnostics Found (meeting severity): {result.Diagnostics.Length}");
    }
}
