using MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;
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
        LoadAllByReflection();
    }

    // Alternative approach: load by reflection
    private static void LoadAllByReflection()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Load sources
            LoadStaticFields<ITaintSource>(assembly, "MauiBlazorAnalyzer.Core.TaintEngine.Sources", _sources);

            // Load sinks
            LoadStaticFields<ITaintSink>(assembly, "MauiBlazorAnalyzer.Core.TaintEngine.Sinks", _sinks);

            // Load sanitizers
            //LoadStaticFields<ITaintSanitizer>(assembly, "MauiBlazorAnalyzer.Core.TaintEngine.Sanitizers", _sanitizers);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading taint analysis patterns: {ex.Message}");
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

}
