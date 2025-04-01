using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using MauiBlazorAnalyzer.Core;


namespace MauiBlazorAnalyzer.Infrastructure;

public class RoslynProjectLoader : IProjectLoader
{
    private readonly ILogger<RoslynProjectLoader> _logger;
    public RoslynProjectLoader(ILogger<RoslynProjectLoader> logger)
    {
        _logger = logger;
    }

    public async Task<ImmutableArray<(Project Project, Compilation? Compilation)>> LoadProjectsAndCompilationsAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var results = ImmutableArray.CreateBuilder<(Project Project, Compilation? Compilation)>();
        try
        {
            _logger.LogInformation("Creating MSBuildWorkspace...");
            using var workspace = MSBuildWorkspace.Create();
            workspace.LoadMetadataForReferencedProjects = true;

            workspace.WorkspaceFailed += (sender, args) => {
                _logger.LogError("Workspace Failed: {Diagnostic}", args.Diagnostic);
            };

            _logger.LogInformation("Loading solution/project from: {InputPath}", inputPath);
            Solution? solution = null;
            if (inputPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                solution = await workspace.OpenSolutionAsync(inputPath, cancellationToken: cancellationToken);
            }
            else if (inputPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = await workspace.OpenProjectAsync(inputPath, cancellationToken: cancellationToken);
                solution = project?.Solution;
            }
            else
            {
                _logger.LogError("Invalid input path. Please provide a .sln or .csproj file.");
                return results.ToImmutable();
            }

            if (solution == null)
            {
                _logger.LogError("Could not load solution or project from path: {InputPath}", inputPath);
                return results.ToImmutable();
            }

            _logger.LogInformation("Processing loaded projects...");
            foreach (var projectId in solution.ProjectIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var project = solution.GetProject(projectId);

                if (project == null || project.Language != LanguageNames.CSharp)
                {
                    _logger.LogDebug("Skipping project '{ProjectName}' (Not C# or not found).", project?.Name ?? "Unknown ID");
                    continue;
                }

                if (!project.Name.Contains("android", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Skipping project '{ProjectName}' (Not Android).", project.Name);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects/compilations from {InputPath}", inputPath);
        }

        return results.ToImmutable();
    }
}
