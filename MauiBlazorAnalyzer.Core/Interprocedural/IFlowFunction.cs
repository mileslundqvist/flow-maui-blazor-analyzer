namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFlowFunction
{
    ISet<IFact> ComputeTargets(IFact inFact);
}

