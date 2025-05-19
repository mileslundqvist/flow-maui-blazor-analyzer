
using MauiBlazorAnalyzer.Application;
using MauiBlazorAnalyzer.Core.Analysis;
using MauiBlazorAnalyzer.Core.Analysis.Interfaces;
using MauiBlazorAnalyzer.Infrastructure;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

class Program
{


    static async Task<int> Main(string[] args)
    {

        RegisterMsBuildLocator();

        RootCommand rootCommand = ParseCommandLineOptions();

        int returnValue = await rootCommand.InvokeAsync(args);

        return returnValue;
    }


    private static async Task RunAnalysisAsync(AnalysisOptions options)
    {
        var services = new ServiceCollection();

        services.AddLogging(configure => configure.AddConsole());

        // Register Infrastructure Service
        services.AddSingleton<IProjectLoader, ProjectLoader>();

        if (options.OutputFormat.Equals("console", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IReporter, ConsoleReporter>();
        }
        else
        {
            throw new NotImplementedException();
        }

        services.AddTransient<AnalysisOrchestrator>();


        // Build Service Provider
        var serviceProvider = services.BuildServiceProvider();


        // --- Run Analysis ---
        var orchestrator = serviceProvider.GetRequiredService<AnalysisOrchestrator>();
        var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Cancellation requested...");
            cancellationTokenSource.Cancel();
        };

        await orchestrator.RunAnalysisAsync(options, cancellationTokenSource.Token);
    }

    private static RootCommand ParseCommandLineOptions()
    {
        var inputPathOption = new Option<string>(
            name: "--input-path",
            description: "Path to the .sln or .csproj file to analyze.")
        { IsRequired = true };

        var outputFormatOption = new Option<string>(
            name: "--output-format",
            description: "Output format (e.g., console, json).",
            getDefaultValue: () => "console");

        var outputPathOption = new Option<string?>(
           name: "--output-path",
           description: "Path for output file (required for formats other than console).");

        var severityOption = new Option<DiagnosticSeverity>(
            name: "--severity",
            description: "Minimum severity to report (Hidden, Info, Warning, Error).",
            getDefaultValue: () => DiagnosticSeverity.Info);

        var rootCommand = new RootCommand("Maui Blazor Static Analyzer");
        rootCommand.AddOption(inputPathOption);
        rootCommand.AddOption(outputFormatOption);
        rootCommand.AddOption(outputPathOption);
        rootCommand.AddOption(severityOption);

        rootCommand.SetHandler(async (context) =>
        {
            var options = new AnalysisOptions
            {
                InputPath = context.ParseResult.GetValueForOption(inputPathOption)!,
                OutputFormat = context.ParseResult.GetValueForOption(outputFormatOption)!,
                OutputPath = context.ParseResult.GetValueForOption(outputPathOption),
                MinimumSeverity = context.ParseResult.GetValueForOption(severityOption)
            };
            await RunAnalysisAsync(options);
        });

        return rootCommand;

    }


    private static void RegisterMsBuildLocator()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            var instance = MSBuildLocator.QueryVisualStudioInstances().OrderByDescending(i => i.Version).FirstOrDefault();
            if (instance == null)
            {
                Console.Error.WriteLine("Error: No compatible MSBuild instance found. Ensure .NET SDK is installed.");
                Environment.Exit(1);
            }
            MSBuildLocator.RegisterInstance(instance);
        }
    }

}