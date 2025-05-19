using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Flow;
using MauiBlazorAnalyzer.Core.Flow.DB;
using MauiBlazorAnalyzer.Core.Flow.FlowFunctions;

namespace MauiBlazorAnalyzer.Core.Flow.FlowFunctions;
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
