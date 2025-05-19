using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;
public interface IFlowFunction
{
    ISet<IFact> ComputeTargets(IFact inFact);
}

