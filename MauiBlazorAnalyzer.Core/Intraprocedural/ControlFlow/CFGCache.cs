using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Concurrent;


namespace MauiBlazorAnalyzer.Core.Intraprocedural.ControlFlow;
public class CFGCache
{
    private readonly ConcurrentDictionary<IMethodSymbol, ControlFlowGraph> _controlFlowGraphCache;

    public CFGCache()
    {
        
    }

}
