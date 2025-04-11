using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Intraprocedural.CallGraph;
public class CallGraph
{
    private readonly Dictionary<IMethodSymbol, HashSet<IMethodSymbol>> _callGraph =
        new Dictionary<IMethodSymbol, HashSet<IMethodSymbol>>(SymbolEqualityComparer.Default);

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

        var displayFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;
        // var displayFormat = SymbolDisplayFormat.CSharpErrorMessageFormat; // Alternative

        // Order by the display string representation for consistency
        foreach (var kvp in _callGraph.OrderBy(kv => kv.Key.ToDisplayString(displayFormat)))
        {
            var caller = kvp.Key;
            var callees = kvp.Value;

            string callerString = caller.ToDisplayString(displayFormat);
            string locationHint = "";
            string contextHint = "";

            if (caller.MethodKind == MethodKind.AnonymousFunction)
            {
                callerString = "lambda expression";

                // Get Location
                var location = caller.Locations.FirstOrDefault();
                if (location != null && location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    locationHint = $" (in {Path.GetFileName(lineSpan.Path)}:{lineSpan.StartLinePosition.Line + 1})";
                }

                var container = caller.ContainingSymbol;
                if (container != null)
                {
                    contextHint = $" (defined in {container.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)})";
                }
            }

            // Print caller with potential context/location
            output.WriteLine($"{callerString}{contextHint}{locationHint} calls:");

            foreach (var callee in callees.OrderBy(c => c.ToDisplayString(displayFormat)))
            {
                output.WriteLine($"  - {callee.ToDisplayString(displayFormat)}");
            }
        }
        output.WriteLine("--- End Call Graph ---");
    }
}
