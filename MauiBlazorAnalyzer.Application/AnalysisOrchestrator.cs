using MauiBlazorAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Application;

public class AnalysisOrchestrator
{
    private readonly IProjectLoader _projectLoader;
    private readonly IEnumerable<IAnalyzer> _allAnalyzers;
    private readonly IReporter _reporter;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        IProjectLoader projectLoader,
        IEnumerable<IAnalyzer> analyzers,
        IReporter reporter,
        ILogger<AnalysisOrchestrator> logger)
    {
        _projectLoader = projectLoader;
        _allAnalyzers = analyzers;
        _reporter = reporter;
        _logger = logger;
    }

    public async Task RunAnalysisAsync(AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var allDiagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        int totalFiles = 0;
        int csharpFiles = 0;
        int razorFiles = 0;
        string? firstErrorMessage = null;

        try
        {
            _logger.LogInformation("Starting analysis run...");
            _logger.LogInformation("Loading projects and compilations...");
            var projectsAndCompilations = await _projectLoader.LoadProjectsAndCompilationsAsync(options.InputPath, cancellationToken);

            if (projectsAndCompilations.IsEmpty)
            {
                throw new InvalidOperationException("No C# projects with compilations could be loaded.");
            }

            // --- Analyzer Filtering ---
            var analyzersToRun = FilterAnalyzers(options).ToList();
            if (!analyzersToRun.Any())
            {
                _logger.LogWarning("No analyzers selected or available to run based on options.");
            }
            else
            {
                _logger.LogInformation("Selected Analyzers: {AnalyzerIds}", string.Join(", ", analyzersToRun.Select(a => a.Id)));
            }


            // --- Execution ---
            foreach (var (project, compilation) in projectsAndCompilations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (compilation == null)
                {
                    _logger.LogWarning("Skipping analysis for project '{ProjectName}' due to missing compilation.", project.Name);
                    continue;
                }

                _logger.LogInformation("Analyzing project: {ProjectName}", project.Name);
                totalFiles += project.DocumentIds.Count + project.AdditionalDocumentIds.Count; 
                csharpFiles += project.Documents.Count(d => d.SourceCodeKind == SourceCodeKind.Regular);
                razorFiles += project.AdditionalDocuments.Count();

                foreach (var analyzer in analyzersToRun)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogDebug("Running analyzer '{AnalyzerId}' on project '{ProjectName}'...", analyzer.Id, project.Name);
                    try
                    {
                        var diagnostics = await analyzer.AnalyzeCompilationAsync(project, compilation, cancellationToken);
                        allDiagnostics.AddRange(diagnostics);
                        _logger.LogDebug("Analyzer '{AnalyzerId}' completed, found {Count} diagnostics.", analyzer.Id, diagnostics.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Analyzer '{AnalyzerId}' failed on project '{ProjectName}'.", analyzer.Id, project.Name);
                        firstErrorMessage ??= $"Analyzer {analyzer.Id} failed: {ex.Message}";
                    }
                }
            }

            // --- Aggregation & Filtering ---
            var finalDiagnostics = allDiagnostics.Where(d => d.Severity >= options.MinimumSeverity).ToImmutableArray();
            _logger.LogInformation("Analysis phase completed. Found {InitialCount} issues, {FinalCount} meet minimum severity '{MinSeverity}'.",
                                   allDiagnostics.Count, finalDiagnostics.Length, options.MinimumSeverity);


            // --- Reporting ---
            stopwatch.Stop();
            var stats = new AnalysisStatistics
            {
                TotalFilesAnalyzed = totalFiles,
                CSharpFilesAnalyzed = csharpFiles,
                RazorFilesAnalyzed = razorFiles,
                AnalysisDuration = stopwatch.Elapsed
            };

            AnalysisResult result;
            if (firstErrorMessage != null)
            {
                result = new AnalysisResult($"Analysis completed with errors: {firstErrorMessage}", stats);
            }
            else
            {
                result = new AnalysisResult(finalDiagnostics, stats);
            }

            _logger.LogInformation("Reporting results...");
            await _reporter.ReportAsync(result, options, cancellationToken);
            _logger.LogInformation("Analysis run finished.");

        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Analysis was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled error occurred during analysis.");
            stopwatch.Stop();
            var stats = new AnalysisStatistics { AnalysisDuration = stopwatch.Elapsed };
            var errorResult = new AnalysisResult($"Critical analysis failure: {ex.Message}", stats);
            await _reporter.ReportAsync(errorResult, options, CancellationToken.None);
        }
    }

    private IEnumerable<IAnalyzer> FilterAnalyzers(AnalysisOptions options)
    {
        IEnumerable<IAnalyzer> filtered = _allAnalyzers;

        // Apply Includes if specified
        if (options.IncludeAnalyzers.Any())
        {
            filtered = filtered.Where(a => options.IncludeAnalyzers.Contains(a.Id));
        }

        // Apply Excludes
        if (options.ExcludeAnalyzers.Any())
        {
            filtered = filtered.Where(a => !options.ExcludeAnalyzers.Contains(a.Id));
        }

        return filtered;
    }
}