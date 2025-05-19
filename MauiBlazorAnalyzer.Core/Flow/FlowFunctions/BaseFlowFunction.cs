using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Flow;
using MauiBlazorAnalyzer.Core.Flow.DB;

namespace MauiBlazorAnalyzer.Core.Flow.FlowFunctions;
internal abstract class BaseFlowFunction : IFlowFunction
{
    protected readonly ICFGEdge Edge;
    protected readonly TaintSpecDB DB;
    protected readonly List<EntryPointInfo> EntryPoints;

    protected BaseFlowFunction(ICFGEdge edge, TaintSpecDB dB, List<EntryPointInfo> entryPoints)
    {
        Edge = edge;
        DB = dB;
        EntryPoints = entryPoints;
    }

    protected bool IsZero(IFact fact) => fact is ZeroFact;
    protected static ISet<IFact> Empty => new HashSet<IFact>();
    public abstract ISet<IFact> ComputeTargets(IFact inFact);
}
