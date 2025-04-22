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

        //var assign = Edge.From.Operation as ISimpleAssignmentOperation;
        //if (assign == null) return new HashSet<TaintFact> { inFact };
        //if (!inFact.IsReturnValue) return new HashSet<TaintFact> { inFact };

        //var dstSymbol = assign.Target switch
        //{
        //    ILocalReferenceOperation l => l.Local as ISymbol,
        //    IParameterReferenceOperation p => p.Parameter,
        //    IFieldReferenceOperation f => f.Field,
        //    _ => null
        //};

        //return dstSymbol == null
        //    ? new HashSet<TaintFact> { inFact }
        //    : new HashSet<TaintFact> { inFact, inFact.WithNewBase(dstSymbol) };

        var outSet = new HashSet<TaintFact>();
        var returnSiteOp = Edge.To.Operation;

        if (inFactAtCallSite.IsReturnValue || inFactAtCallSite.Path is null)
        {
            return Empty; // Non-path facts don't bypass the callee this way.
        }

        if (returnSiteOp is ISimpleAssignmentOperation assign)
        {
            if (inFactAtCallSite.AppliesTo(assign.Target))
            {
                outSet.Remove(inFactAtCallSite);

            }
        }
        return outSet;
    }
}
