using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class CallToReturnFlow : BaseFlowFunction
{
    public CallToReturnFlow(ICFGEdge edge, TaintSpecDB dB) : base(edge, dB)
    {
    }

    public override ISet<TaintFact> ComputeTargets(TaintFact inFact)
    {
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
        outSet.Add(inFact);
        return outSet;
    }
}
