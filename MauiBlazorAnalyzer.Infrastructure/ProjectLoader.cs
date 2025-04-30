using MauiBlazorAnalyzer.Core.Analysis.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Infrastructure;


/// <summary>
/// Loads projects and compilations from path
/// </summary>
public class ProjectLoader : IProjectLoader
{
    private readonly ILogger<ProjectLoader> _logger;
    public ProjectLoader(ILogger<ProjectLoader> logger)
    {
        _logger = logger;
    }

    public async Task<ImmutableArray<(Project Project, Compilation? Compilation)>> LoadProjectsAndCompilationsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var results = ImmutableArray.CreateBuilder<(Project project, Compilation? Compilation)>();

        try
        {
            using MSBuildWorkspace workspace = CreateMsBuildWorkspace();


            Solution? solution = await LoadSolution(workspace, inputPath, cancellationToken);

            if (solution == null)
            {
                _logger.LogError("Could not load solution or project from path: {InputPath}", inputPath);
                return results.ToImmutable();
            }

            _logger.LogInformation("Processing loaded projects...");
            await LoadProjectCompilationsAsync(results, solution, cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects/compilations from {InputPath}", inputPath);
        }

        return results.ToImmutable();
    }

    private async Task LoadProjectCompilationsAsync(ImmutableArray<(Project project, Compilation? Compilation)>.Builder results, Solution solution, CancellationToken cancellationToken)
    {
        foreach (var projectId in solution.ProjectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = solution.GetProject(projectId);

            if (project == null || project.Language != LanguageNames.CSharp)
            {
                continue;
            }

            if (!IsAndroidBuild(project))
            {
                continue;
            }

            _logger.LogInformation("Getting compilation for project '{ProjectName}'...", project.Name);
            var compilation = await project.GetCompilationAsync(cancellationToken);

            if (compilation == null)
            {
                _logger.LogWarning("Could not get compilation for project '{ProjectName}'. Skipping analysis for this project.", project.Name);
            }
            else
            {
                _logger.LogInformation("Successfully obtained compilation for '{ProjectName}'.", project.Name);
            }

            results.Add((project, compilation));
        }
    }

    private async Task<Solution?> LoadSolution(MSBuildWorkspace workspace, string inputPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading solution/project from: {InputPath}", inputPath);
        if (inputPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await workspace.OpenSolutionAsync(inputPath, cancellationToken: cancellationToken);
        }
        else if (inputPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(inputPath, cancellationToken: cancellationToken);
            return project?.Solution;
        }
        else
        {
            _logger.LogError("Invalid input path. Please provide a .sln or .csproj file.");
            return null;
        }
    }

    private static bool IsAndroidBuild(Project project)
        => project.ParseOptions is CSharpParseOptions cpo &&
        cpo.PreprocessorSymbolNames.Any(s => s.Equals("ANDROID", StringComparison.OrdinalIgnoreCase));

    private MSBuildWorkspace CreateMsBuildWorkspace()
    {
        _logger.LogInformation("Creating MSBuildWorkspace...");
        var workspace = MSBuildWorkspace.Create();
        workspace.LoadMetadataForReferencedProjects = true;

        workspace.WorkspaceFailed += (sender, args) =>
        {
            _logger.LogError("Workspace Failed: {Diagnostic}", args.Diagnostic);
        };
        return workspace;
    }
}
