using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;
public interface IFDSTabulationProblem
{
    IReadOnlyDictionary<ICFGNode, ISet<IFact>> InitialSeeds { get; }
    ZeroFact ZeroValue { get; }
    InterproceduralCFG Graph { get; }
    IFlowFunctions FlowFunctions { get; }
}
