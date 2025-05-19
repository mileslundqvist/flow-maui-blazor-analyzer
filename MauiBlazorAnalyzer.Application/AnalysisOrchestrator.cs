using MauiBlazorAnalyzer.Core.Analysis;
using MauiBlazorAnalyzer.Core.Analysis.Interfaces;
using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MauiBlazorAnalyzer.Application;
public class AnalysisOrchestrator
{

    private readonly IProjectLoader _projectLoader;
    private readonly IReporter _reporter;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        IProjectLoader projectLoader,
        IReporter reporter,
        ILogger<AnalysisOrchestrator> logger)
    {
        _projectLoader = projectLoader;
        _reporter = reporter;
        _logger = logger;
    }


    public async Task RunAnalysisAsync(AnalysisOptions options, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var overallStatistics = new AnalysisStatistics();
        AnalysisResult? finalResult = null;

        _logger.LogInformation("Starting analysis run...");

        try
        {
            // 1. Load Projects
            var projectsAndCompilations = await LoadProjectsAndCompilationsAsync(options, cancellationToken);

            // 2. Analyze Projects
            var allDiagnostics = await AnalyzeProjectsAsync(projectsAndCompilations, overallStatistics, cancellationToken);

            // 3. Filter Diagnostics
            var filteredDiagnostics = FilterDiagnostics(allDiagnostics, options.MinimumSeverity);
            LogFilteringResults(allDiagnostics.Count(), filteredDiagnostics.Length, options.MinimumSeverity);

            // 4. Create Success Result
            stopwatch.Stop();
            overallStatistics.AnalysisDuration = stopwatch.Elapsed;
            finalResult = AnalysisResult.CreateSuccess(filteredDiagnostics, overallStatistics);

        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Analysis was cancelled.");
            stopwatch.Stop();
            overallStatistics.AnalysisDuration = stopwatch.Elapsed;
            finalResult = AnalysisResult.CreateFailure("Analysis was cancelled.", overallStatistics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "An unhandled error occurred during the analysis orchestration.");
            stopwatch.Stop();
            overallStatistics.AnalysisDuration = stopwatch.Elapsed;
            finalResult = AnalysisResult.CreateFailure($"Critical analysis failure: {ex.Message}", overallStatistics);
        }
        finally
        {
            if (finalResult != null)
            {
                _logger.LogInformation("Reporting analysis results...");
                await ReportResultAsync(finalResult, options, cancellationToken);
            }
            else
            {
                _logger.LogError("Analysis finished but no final result was generated.");
                stopwatch.Stop();
                overallStatistics.AnalysisDuration = stopwatch.Elapsed;
                var fallbackResult = AnalysisResult.CreateFailure("Analysis completed in an unexpected state.", overallStatistics);
                await ReportResultAsync(fallbackResult, options, CancellationToken.None);
            }
            _logger.LogInformation("Analysis run finished in {Duration}.", overallStatistics.AnalysisDuration);
        }
    }


    private async Task<ProjectAnalysisResult> AnalyzeProjectAsync(Project project, Compilation compilation, CancellationToken cancellationToken)
    {

        var analyzer = new BlazorEntryPointAnalyzer(compilation);
        List<EntryPointInfo> entryPoints = analyzer.FindEntryPoints();
        var taintAnalysisProblem = new TaintAnalysisProblem(project, compilation, entryPoints);

        var solver = new IFDSSolver(taintAnalysisProblem);
        var result = await solver.Solve();

        var reporter = new TaintDiagnosticReporter(result, taintAnalysisProblem.Graph);

        //foreach (AnalysisDiagnostic d in reporter.ToDiagnostics())
        //{
        //    Console.WriteLine($"{d.Severity}: {d.Message} @ {d.FilePath}:{d.Location.StartLinePosition.Line + 1}");
        //}
        var diagnostics = reporter.ToDiagnostics();


        return new ProjectAnalysisResult(diagnostics.ToImmutableArray()); // TODO: Handle Correctly
    }


    private async Task<ImmutableArray<AnalysisDiagnostic>> AnalyzeProjectsAsync(
        ImmutableArray<(Project Project, Compilation? Compilation)> projectsAndCompilations, 
        AnalysisStatistics overallStatistics, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting analysis of loaded projects...");
        var allDiagnosticsBuilder = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        int projectsAnalyzedCount = 0;

        foreach (var (project, compilation) in projectsAndCompilations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation == null)
            {
                _logger.LogWarning("Skipping analysis for project '{ProjectName}' due to missing compilation.", project.Name);
                continue;
            }

            _logger.LogInformation("Analyzing project: {ProjectName}", project.Name);
            try
            {
                var projectResult = await AnalyzeProjectAsync(project, compilation, cancellationToken);

                allDiagnosticsBuilder.AddRange(projectResult.Diagnostics);
                projectsAnalyzedCount++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze project {ProjectName}. Skipping this project.", project.Name);
            }
        }

        _logger.LogInformation("Analyzed {AnalyzedCount} out of {TotalCount} projects.", projectsAnalyzedCount, projectsAndCompilations.Count());
        return allDiagnosticsBuilder.ToImmutable();
    }


    private async Task<ImmutableArray<(Project Project, Compilation? Compilation)>> LoadProjectsAndCompilationsAsync(AnalysisOptions options, CancellationToken cancellationToken)
    {
        var projectsAndCompilations = await _projectLoader.LoadProjectsAndCompilationsAsync(options.InputPath, cancellationToken);
        if (projectsAndCompilations.IsEmpty) throw new InvalidOperationException("No C# projects with compilations could be loaded.");
        return projectsAndCompilations;
    }


    private ImmutableArray<AnalysisDiagnostic> FilterDiagnostics(ImmutableArray<AnalysisDiagnostic> diagnostics, DiagnosticSeverity minimumSeverity)
    {
        return diagnostics.Where(d => d.Severity >= minimumSeverity).ToImmutableArray();
    }


    private void LogFilteringResults(int initialCount, int finalCount, DiagnosticSeverity minimumSeverity)
    {
        _logger.LogInformation("Analysis phase generated {InitialCount} raw diagnostics. Filtered down to {FinalCount} diagnostics matching minimum severity '{MinSeverity}'.",
                             initialCount, finalCount, minimumSeverity);
    }


    private async Task ReportResultAsync(AnalysisResult result, AnalysisOptions options, CancellationToken cancellationToken)
    {
        var reportingToken = result.Succeeded ? cancellationToken : CancellationToken.None;
        try
        {
            await _reporter.ReportAsync(result, options, reportingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report analysis results.");
        }
    }
}
