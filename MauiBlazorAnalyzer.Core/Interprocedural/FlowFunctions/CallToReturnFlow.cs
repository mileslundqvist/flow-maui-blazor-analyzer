using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class CallToReturnFlow : BaseFlowFunction
{
    public CallToReturnFlow(ICFGEdge edge, TaintSpecDB dB) : base(edge, dB)
    {
    }

    public override ISet<TaintFact> ComputeTargets(TaintFact inFactAtCallSite)
    {
        if (IsZero(inFactAtCallSite)) return Empty; // Should not be called with ZeroFact

        var outSet = new HashSet<TaintFact> { inFactAtCallSite };
       
        return outSet;
    }
}
