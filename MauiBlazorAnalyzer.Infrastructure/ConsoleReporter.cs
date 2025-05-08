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
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("               TAINT ANALYSIS REPORT               ");
        Console.WriteLine("═══════════════════════════════════════════════════");


        if (!result.Succeeded)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ANALYSIS FAILED]: {result.ErrorMessage}");
            Console.ResetColor();
        }

        PrintAnalysisStatistics(result); // Renamed for clarity

        if (result.Diagnostics.Any())
        {
            PrintDiagnostics(result, cancellationToken); // Renamed for clarity
        }
        else if (result.Succeeded)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n✅ No taint vulnerabilities found matching the criteria.");
            Console.ResetColor();
        }

        Console.WriteLine("\n--- End of Taint Analysis Report ---");
        _logger.LogInformation("Console report generated for taint analysis.");
        return Task.CompletedTask;
    }

    private static void PrintDiagnostics(AnalysisResult result, CancellationToken cancellationToken)
    {
        Console.WriteLine("\n[POTENTIAL VULNERABILITIES DETECTED]");
        Console.WriteLine("───────────────────────────");

        // Group by file for better readability might still be useful if many diagnostics overall,
        // but for taint, each diagnostic message is now quite long and self-contained.
        // Let's print them sequentially, ordered by severity then file/line.

        var sortedDiagnostics = result.Diagnostics
            .OrderByDescending(d => d.Severity) // Show errors/warnings first
            .ThenBy(d => d.FilePath ?? string.Empty)
            .ThenBy(d => d.Location.StartLinePosition.Line)
            .ThenBy(d => d.Location.StartLinePosition.Character);

        int findingCount = 0;
        foreach (var diag in sortedDiagnostics)
        {
            findingCount++;
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine($"\n--- Finding #{findingCount} ---");

            string severityString = diag.Severity.ToString().ToUpperInvariant();
            ConsoleColor color = diag.Severity switch
            {
                DiagnosticSeverity.Error => ConsoleColor.Red,
                DiagnosticSeverity.Warning => ConsoleColor.Yellow,
                DiagnosticSeverity.Info => ConsoleColor.Cyan,
                _ => Console.ForegroundColor
            };

            Console.ForegroundColor = color;
            Console.Write($"{severityString} [{diag.Id}]");
            Console.ResetColor();
            Console.WriteLine($" at {diag.Location.Path}({diag.Location.StartLinePosition.Line + 1},{diag.Location.StartLinePosition.Character + 1})");
            Console.WriteLine($"  Title: {diag.Title}");

            // The message is now multi-line and contains the detailed trace.
            // Indent the message slightly for better structure under the title.
            Console.ForegroundColor = ConsoleColor.Gray; // Subtle color for the detailed message body
            // Split the message by lines and print each one indented
            var messageLines = diag.Message.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (var line in messageLines)
            {
                Console.WriteLine($"  {line}");
            }
            Console.ResetColor();

            if (!string.IsNullOrEmpty(diag.HelpLink)) Console.WriteLine($"  Help: {diag.HelpLink}");
            Console.WriteLine("----------------------");
        }
    }

    private static void PrintAnalysisStatistics(Core.Analysis.AnalysisResult result)
    {
        Console.WriteLine("\n[Analysis Statistics]");
        Console.WriteLine("─────────────────────");
        Console.WriteLine($"- Analysis Duration: {result.Statistics.AnalysisDuration}");
        // Assuming these might be added back or are relevant:
        // Console.WriteLine($"- Total Files Analyzed: {result.Statistics.TotalFilesAnalyzed}");
        // Console.WriteLine($"- C# Files Analyzed: {result.Statistics.CSharpFilesAnalyzed}");
        // Console.WriteLine($"- Razor Files Analyzed: {result.Statistics.RazorFilesAnalyzed}");
        Console.WriteLine($"- Vulnerabilities Found: {result.Diagnostics.Length}");
    }
}
