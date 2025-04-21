using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural.DB;
internal sealed class TaintSpecDB
{
    private TaintSpecDB() { /* load JSON or attributes */ }
    public static TaintSpecDB Instance { get; } = new();

    public bool IsSource(IMethodSymbol m) => _sources.Contains(m.ToDisplayString());
    public bool IsSink(IMethodSymbol m) => _sinks.Contains(m.ToDisplayString());
    public bool IsSanitizer(IMethodSymbol m) => _sanitizers.Contains(m.ToDisplayString());

    private readonly HashSet<string> _sources = new();
    private readonly HashSet<string> _sinks = new();
    private readonly HashSet<string> _sanitizers = new();
}
