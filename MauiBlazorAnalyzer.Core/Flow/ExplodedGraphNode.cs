using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;
public readonly record struct ExplodedGraphNode(ICFGNode Node, IFact Fact);