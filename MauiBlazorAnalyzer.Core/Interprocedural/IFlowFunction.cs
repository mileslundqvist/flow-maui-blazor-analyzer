namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFlowFunction
{
    ISet<TaintFact> ComputeTargets(TaintFact inFact);
}

