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

        if (inFact.IsReturnValue || inFact.Path is null)
        {
            return Empty;
        }

        outSet.Add(inFact);

        switch (operation)
        {
            case ISimpleAssignmentOperation assign when inFact.AppliesTo(assign.Value):
                // Propagate taint from the RHS value to the LHS target.
                if (inFact.AppliesTo(assign.Value))
                {
                    var dstSymbol = assign.Target switch
                    {
                        ILocalReferenceOperation l => l.Local as ISymbol,
                        IParameterReferenceOperation p => p.Parameter,
                        IFieldReferenceOperation f => f.Field,
                        IPropertyReferenceOperation pr => pr.Property,
                        _ => null
                    };

                    if (dstSymbol != null)
                    {
                        // Create a new fact rooted at the destination symbol
                        outSet.Add(inFact.WithNewBase(dstSymbol));
                    }
                }

                if (inFact.AppliesTo(assign.Target))
                {
                    outSet.Remove(inFact);
                }

                if (assign.Value is IInvocationOperation inv && DB.IsSanitizer(inv.TargetMethod))
                {

                    if (inFact.AppliesTo(inv))
                    {
                        outSet.Remove(inFact); // Kill the incoming taint if it's sanitized
                    }
                    // Also, the sanitizer's *output* should not be tainted unless it's a source/passthrough sanitizer.
                    // This is handled by the ReturnFlow of the sanitizer call (if it's analyzed)
                    // or the definition of the sanitizer source/passthrough in TaintSpecDB.
                }
                break;
        }
        return outSet;
    }
}
