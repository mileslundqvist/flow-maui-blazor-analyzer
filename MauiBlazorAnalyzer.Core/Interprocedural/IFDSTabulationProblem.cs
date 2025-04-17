using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFDSTabulationProblem
{
    IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> InitialSeeds { get; }
    ZeroFact ZeroValue { get; }
    InterproceduralCFG Graph { get; }
    IFlowFunctions FlowFunctions { get; }
}
