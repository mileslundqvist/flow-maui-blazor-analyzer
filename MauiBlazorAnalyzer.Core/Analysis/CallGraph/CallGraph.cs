using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Analysis.CallGraph;
public class CallGraph
{
    private readonly Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>> _callGraph =
        new Dictionary<MethodAnalysisContext, HashSet<MethodAnalysisContext>>();

    public void AddEdge(MethodAnalysisContext caller, MethodAnalysisContext callee)
    {
        if (caller == null || callee == null) return;

        if (!_callGraph.TryGetValue(caller, out var callees))
        {
            callees = new HashSet<MethodAnalysisContext>();
            _callGraph[caller] = callees;
        }

        callees.Add(callee);
    }

    public IEnumerable<MethodAnalysisContext> GetCallers() => _callGraph.Keys;

    public IEnumerable<MethodAnalysisContext> GetCallees(MethodAnalysisContext symbol)
    {
        if (_callGraph.TryGetValue(symbol, out var callees))
        {
            return callees;
        }
        return Enumerable.Empty<MethodAnalysisContext>();

    }

    public void Print(TextWriter output)
    {
        output.WriteLine("--- Call Graph ---");

        var displayFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;
        // var displayFormat = SymbolDisplayFormat.CSharpErrorMessageFormat; // Alternative

        // Order by the display string representation for consistency
        foreach (var kvp in _callGraph.OrderBy(kv => kv.Key.MethodSymbol.ToDisplayString(displayFormat)))
        {
            var caller = kvp.Key;
            var callees = kvp.Value;

            string callerString = caller.MethodSymbol.ToDisplayString(displayFormat);
            string locationHint = "";
            string contextHint = "";

            if (caller.MethodSymbol.MethodKind == MethodKind.AnonymousFunction)
            {
                callerString = "lambda expression";

                // Get Location
                var location = caller.MethodSymbol.Locations.FirstOrDefault();
                if (location != null && location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    locationHint = $" (in {Path.GetFileName(lineSpan.Path)}:{lineSpan.StartLinePosition.Line + 1})";
                }

                var container = caller.MethodSymbol.ContainingSymbol;
                if (container != null)
                {
                    contextHint = $" (defined in {container.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)})";
                }
            }

            // Print caller with potential context/location
            output.WriteLine($"{callerString}{contextHint}{locationHint} calls:");

            foreach (var callee in callees.OrderBy(c => c.MethodSymbol.ToDisplayString(displayFormat)))
            {
                output.WriteLine($"  - {callee.MethodSymbol.ToDisplayString(displayFormat)}");
            }
        }
        output.WriteLine("--- End Call Graph ---");
    }
}
