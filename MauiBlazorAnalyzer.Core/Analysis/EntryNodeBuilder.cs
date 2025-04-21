using MauiBlazorAnalyzer.Core.Interprocedural;
using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Analysis;
public static class EntryNodeBuilder
{
    public static async Task<IEnumerable<ICFGNode>> BuildEntryNodesAsync(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        if (compilation is null) throw new ArgumentNullException(nameof(compilation));

        // 1. Heuristic root discovery (MAUI / Blazor / JS‑Interop, etc.)
        var rootMethods = MauiEntryPointLocator.FindEntryPoints(compilation)
                                              .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                                              .ToList();
                                              
         
        // 2. Resolve MethodAnalysisContexts (with SemanticModel & IOperation)
        var dict = await MethodAnalysisContextProvider.GetMethodAnalysisContexts(compilation, cancellationToken);

        // 3. Build ICFG entry nodes
        var entryNodes = new List<ICFGNode>();
        foreach (var method in rootMethods)
        {
            if (!dict.TryGetValue(method, out var ctx))
            {
                // Fallback: construct a minimal context (no root operation)
                ctx = new MethodAnalysisContext(method, operation: null);
            }

            var entryNode = new ICFGNode(operation: null, ctx, ICFGNodeKind.Entry);
            entryNodes.Add(entryNode);
        }
        return entryNodes;
    }
}
