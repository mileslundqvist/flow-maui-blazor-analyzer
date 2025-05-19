using MauiBlazorAnalyzer.Core.Analysis.Interfaces;
using Microsoft.Build.Construction;
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

    private async Task LoadProjectCompilationsAsync(
        ImmutableArray<(Project project, Compilation? Compilation)>.Builder results,
        Solution solution,
        CancellationToken cancellationToken)
    {

        Project? mauiBlazorAndroidProject = null;

        _logger.LogInformation("Searching for the .NET MAUI Blazor Hybrid Android project");


        foreach (var projectId in solution.ProjectIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = solution.GetProject(projectId);

            if (project == null || project.Language != LanguageNames.CSharp)
            {
                continue;
            }

            if (IsPotentialMauiBlazorHybridAndroidProject(project))
            {
                _logger.LogInformation("Identified potential .NET MAUI Blazor Hybrid Android project: {ProjectName} ({ProjectPath})", project.Name, project.FilePath);
                mauiBlazorAndroidProject = project;
                break;
            }
        }

        if (mauiBlazorAndroidProject == null)
        {
            _logger.LogWarning("No .NET MAUI Blazor Hybrid Android project could be identified in the solution based on current criteria (MAUI and ANDROID preprocessor symbols). Ensure the project is configured correctly.");
            return;
        }

        // 2. Collect the main project and its transitive dependencies within the solution
        var projectsToLoad = new HashSet<Project>();
        var dependencyGraph = solution.GetProjectDependencyGraph();

        _logger.LogInformation("Adding main project '{ProjectName}' to the list of projects to load.", mauiBlazorAndroidProject.Name);
        projectsToLoad.Add(mauiBlazorAndroidProject);

        _logger.LogInformation("Identifying transitive project dependencies for '{ProjectName}'...", mauiBlazorAndroidProject.Name);
        var transitiveDependencyIds = dependencyGraph.GetProjectsThatThisProjectTransitivelyDependsOn(mauiBlazorAndroidProject.Id);

        _logger.LogInformation("Found {Count} transitive project dependencies.", transitiveDependencyIds.Count());
        foreach (var depId in transitiveDependencyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependentProject = solution.GetProject(depId);
            if (dependentProject != null && dependentProject.Language == LanguageNames.CSharp)
            {
                _logger.LogInformation("Adding dependency project '{DependentProjectName}' to the list.", dependentProject.Name);
                projectsToLoad.Add(dependentProject);
            }
            else if (dependentProject != null)
            {
                _logger.LogDebug("Skipping non-CSharp dependency project '{DependentProjectName}'.", dependentProject.Name);
            }
        }

        _logger.LogInformation("Total projects to load (MAUI Blazor app and its C# dependencies): {Count}", projectsToLoad.Count);

        // 3. Load compilations for these selected projects
        foreach (var projectToCompile in projectsToLoad)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Attempting to get compilation for project '{ProjectName}' ({ProjectPath})", projectToCompile.Name, projectToCompile.FilePath);

            Compilation? compilation = await projectToCompile.GetCompilationAsync(cancellationToken);

            if (compilation == null)
            {
                _logger.LogWarning("Could not get compilation for project '{ProjectName}'. Analysis for this project might be incomplete.", projectToCompile.Name);
            }
            else if (!(compilation is CSharpCompilation))
            {
                _logger.LogWarning("Compilation for project '{ProjectName}' is not a CSharpCompilation. Skipping.", projectToCompile.Name);
                compilation = null; // Ensure we don't add a non-CSharp compilation
            }
            else
            {
                _logger.LogInformation("Successfully obtained C# compilation for '{ProjectName}'.", projectToCompile.Name);
            }
            results.Add((projectToCompile, compilation));
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

    private bool IsPotentialMauiBlazorHybridAndroidProject(Project project)
    {
        if (project.Language != LanguageNames.CSharp)
        {
            return false;
        }

        if (project.ParseOptions is CSharpParseOptions csharpParseOptions)
        {
            var symbols = csharpParseOptions.PreprocessorSymbolNames;
            bool hasAndroidSymbol = symbols.Any(s => s.Equals("ANDROID", StringComparison.OrdinalIgnoreCase));

            var xml = ProjectRootElement.Open(project.FilePath);

            // helper: get the *last* value for a given property name
            static string? GetProp(ProjectRootElement xml, string name)
                => xml.Properties
                      .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                      .Select(p => p.Value)
                      .LastOrDefault();

            if (hasAndroidSymbol)
            {
                var useMaui = GetProp(xml, "UseMaui");

                if (useMaui != null)
                {
                    _logger.LogDebug("Project '{ProjectName}' has MAUI and ANDROID preprocessor symbols.", project.Name);
                    return true;
                }
            }
            else
            {
                _logger.LogDebug("Project '{ProjectName}' missing ANDROID symbol. ANDROID: {HasAndroidSymbol}", project.Name, hasAndroidSymbol);
            }
        }
        else
        {
            _logger.LogDebug("Project '{ProjectName}' does not have CSharpParseOptions to check preprocessor symbols.", project.Name);
        }

        return false;
    }

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
