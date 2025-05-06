using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class CallFlow : BaseFlowFunction
{


    public CallFlow(ICFGEdge edge, TaintSpecDB dB, List<EntryPointInfo> entryPoints) : base(edge, dB, entryPoints)
    {
    }

    public override ISet<IFact> ComputeTargets(IFact inFactAtCallSite)
    {
       HashSet<IFact> outFacts = new HashSet<IFact>();

        return outFacts;


    }
}
