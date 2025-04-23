using MauiBlazorAnalyzer.Core.Interprocedural.DB;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal abstract class BaseFlowFunction : IFlowFunction
{
    protected readonly ICFGEdge Edge;
    protected readonly TaintSpecDB DB;

    protected BaseFlowFunction(ICFGEdge edge, TaintSpecDB dB)
    {
        Edge = edge;
        DB = dB;
    }

    protected bool IsZero(IFact fact) => fact is ZeroFact;
    protected static ISet<TaintFact> Empty => new HashSet<TaintFact>();
    public abstract ISet<TaintFact> ComputeTargets(TaintFact inFact);
}
