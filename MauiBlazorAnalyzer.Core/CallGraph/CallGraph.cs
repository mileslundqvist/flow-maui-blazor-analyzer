using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.CallGraph;

public class CallGraph
{
    private readonly Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> _callGraph =
        new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);
    internal CallGraph() { }

    public void AddEdge(IMethodSymbol caller, IMethodSymbol callee)
    {
        if (caller == null || callee == null) return;

        if (!_callGraph.TryGetValue(caller, out var callees))
        {
            callees = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            _callGraph[caller] = callees;
        }
        callees.Add(callee);
    }

    public IEnumerable<IMethodSymbol> GetCallers() => _callGraph.Keys;

    public IEnumerable<IMethodSymbol> GetCallees(IMethodSymbol symbol)
    {
        if (_callGraph.TryGetValue(symbol, out var callees))
        {
            return callees;
        }
        return Enumerable.Empty<IMethodSymbol>();

    }

    public void Print(TextWriter output)
    {
        output.WriteLine("--- Call Graph ---");
        
        foreach (var kvp in _callGraph.OrderBy(kv => kv.Key.Name))
        {
            var caller = kvp.Key;
            var callees = kvp.Value;
            output.WriteLine($"{caller.ContainingType.Name}.{caller.Name} calls:");
            foreach (var callee in callees.OrderBy(c => c.Name))
            {
                output.WriteLine($"  - {callee.ContainingType.Name}.{callee.Name}");
            }
        }
        output.WriteLine("--- End Call Graph ---");
    }
}