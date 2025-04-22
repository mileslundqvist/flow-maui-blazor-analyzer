namespace MauiBlazorAnalyzer.Core.Interprocedural;
public readonly record struct ExplodedGraphNode(ICFGNode Node, IFact Fact);