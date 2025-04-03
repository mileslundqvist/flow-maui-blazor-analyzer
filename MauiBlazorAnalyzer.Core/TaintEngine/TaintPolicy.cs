using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
using MauiBlazorAnalyzer.Core.TaintEngine.Sanitizers;
using MauiBlazorAnalyzer.Core.TaintEngine.Sinks;
using MauiBlazorAnalyzer.Core.TaintEngine.Sources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public static class TaintPolicy
{
    private static readonly List<ITaintSource> _sources = new List<ITaintSource>();
    private static readonly List<ITaintSink> _sinks = new List<ITaintSink>();
    private static readonly List<ITaintSanitizer> _sanitizers = new List<ITaintSanitizer>();

    // Static constructor will be called once before the first access to any static member
    static TaintPolicy()
    {
        LoadAllSources();
        LoadAllSinks();
        LoadAllSanitizers();
    }

    private static void LoadAllSources()
    {
        // Add Web Sources
        _sources.Add(WebSources.RequestQuery);
        _sources.Add(WebSources.RequestBody);
        _sources.Add(WebSources.RequestHeader);
        _sources.Add(WebSources.HttpRequest);

        _sources.Add(MauiBlazorSources.BlazorComponentParameters);
        _sources.Add(MauiBlazorSources.JsInteropInput);
        _sources.Add(MauiBlazorSources.UrlParameters);



        // You can add other sources from different categories
        // _sources.Add(FileSystemSources.FileRead);
        // _sources.Add(ConsoleInput.ReadLine);
    }

    private static void LoadAllSinks()
    {
        // Add SQL Sinks
        _sinks.Add(SqlSinks.SqlCommand);
        _sinks.Add(SqlSinks.EntityFramework);
        _sinks.Add(SqlSinks.Dapper);

        _sinks.Add(MauiBlazorSinks.JsInvoke);
        _sinks.Add(MauiBlazorSinks.JsInvokeAsync);
        _sinks.Add(MauiBlazorSinks.JsEval);
        _sinks.Add(MauiBlazorSinks.WebViewSource);
        _sinks.Add(MauiBlazorSinks.WebViewNavigate);
        _sinks.Add(MauiBlazorSinks.HtmlInjection);
        _sinks.Add(MauiBlazorSinks.LocalStorage);
        _sinks.Add(MauiBlazorSinks.FileSystemWrite);
        _sinks.Add(MauiBlazorSinks.PlatformInvoke);
        _sinks.Add(MauiBlazorSinks.NavigationToUri);

        // Add other categories of sinks
        // _sinks.Add(XssSinks.HtmlOutput);
        // _sinks.Add(CommandInjectionSinks.ProcessStart);
    }

    private static void LoadAllSanitizers()
    {
        // Add SQL Sanitizers
        _sanitizers.Add(SqlSanitizers.ParameterizedQuery);
        _sanitizers.Add(SqlSanitizers.OrmFramework);

        // Add other categories of sanitizers
        // _sanitizers.Add(XssSanitizers.HtmlEncode);
    }

    // Alternative approach: load by reflection
    private static void LoadAllByReflection()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Load sources
            LoadStaticFields<ITaintSource>(assembly, "TaintAnalysis.Sources", _sources);

            // Load sinks
            LoadStaticFields<ITaintSink>(assembly, "TaintAnalysis.Sinks", _sinks);

            // Load sanitizers
            LoadStaticFields<ITaintSanitizer>(assembly, "TaintAnalysis.Sanitizers", _sanitizers);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading taint analysis patterns: {ex.Message}");

            // Fallback to manual loading if reflection fails
            LoadAllSources();
            LoadAllSinks();
            LoadAllSanitizers();
        }
    }

    private static void LoadStaticFields<T>(Assembly assembly, string namespaceName, List<T> collection)
    {
        var types = assembly.GetTypes()
            .Where(t => t.Namespace == namespaceName && t.IsClass && t.IsAbstract && t.IsSealed);

        foreach (var type in types)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(f => typeof(T).IsAssignableFrom(f.FieldType));

            foreach (var field in fields)
            {
                if (field.GetValue(null) is T item)
                {
                    collection.Add(item);
                }
            }
        }
    }

    // Public API - now all static methods
    public static bool IsSource(string methodSignature) =>
        _sources.Any(s => s.Matches(methodSignature));

    public static bool IsSink(string methodSignature) =>
        _sinks.Any(s => s.Matches(methodSignature));

    public static bool IsSanitizer(string methodSignature) =>
        _sanitizers.Any(s => s.Matches(methodSignature));

    // Additional useful methods
    public static ITaintSource? GetMatchingSource(string methodSignature) =>
        _sources.FirstOrDefault(s => s.Matches(methodSignature));

    public static ITaintSink? GetMatchingSink(string methodSignature) =>
        _sinks.FirstOrDefault(s => s.Matches(methodSignature));

    public static ITaintSanitizer? GetMatchingSanitizer(string methodSignature) =>
        _sanitizers.FirstOrDefault(s => s.Matches(methodSignature));

    // For diagnostics or testing
    public static IReadOnlyList<ITaintSource> AllSources => _sources.AsReadOnly();
    public static IReadOnlyList<ITaintSink> AllSinks => _sinks.AsReadOnly();
    public static IReadOnlyList<ITaintSanitizer> AllSanitizers => _sanitizers.AsReadOnly();
}
