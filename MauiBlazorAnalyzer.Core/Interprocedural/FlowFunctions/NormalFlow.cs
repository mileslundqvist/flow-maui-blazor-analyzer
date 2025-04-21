using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class NormalFlow : BaseFlowFunction
{
    public NormalFlow(ICFGEdge edge, TaintSpecDB db) : base(edge, db) { }

    public override ISet<TaintFact> ComputeTargets(TaintFact inFact)
    {
        if (IsZero(inFact)) return Empty;

        var operation = Edge.From.Operation;
        var outSet = new HashSet<TaintFact> { inFact };

        switch (operation)
        {
            case ISimpleAssignmentOperation assign when inFact.AppliesTo(assign.Value):
                var dstSymbol = assign.Target switch
                {
                    ILocalReferenceOperation l => l.Local as ISymbol,
                    IParameterReferenceOperation p => p.Parameter,
                    IFieldReferenceOperation f => f.Field,
                    _ => null
                };
                if (dstSymbol != null) outSet.Add(inFact.WithNewBase(dstSymbol));
                break;
            case ISimpleAssignmentOperation { Value: IInvocationOperation inv } assign2:
                if (DB.IsSanitizer(inv.TargetMethod) && inFact.AppliesTo(inv))
                    outSet.Remove(inFact); // kill
                break;
            case IReturnOperation ret:
                if (inFact.AppliesTo(ret.ReturnedValue))
                    outSet.Add(new TaintFact(ret.SemanticModel!.GetEnclosingSymbol(ret.Syntax.SpanStart) as IMethodSymbol));
                break;
        }
        return outSet;
    }
}
