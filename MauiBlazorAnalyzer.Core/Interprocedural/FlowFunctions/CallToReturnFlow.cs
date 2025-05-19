using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class CallToReturnFlow : BaseFlowFunction
{
    public CallToReturnFlow(ICFGEdge edge, TaintSpecDB dB, List<EntryPointInfo> entryPoints) : base(edge, dB, entryPoints)
    {
    }

    public override ISet<IFact> ComputeTargets(IFact inFactAtCallSite)
    {

        var outSet = new HashSet<IFact> { inFactAtCallSite };

        return outSet;
    }
}
