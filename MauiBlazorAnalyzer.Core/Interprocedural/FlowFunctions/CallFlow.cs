using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class CallFlow : BaseFlowFunction
{
    public CallFlow(ICFGEdge edge, TaintSpecDB dB) : base(edge, dB)
    {
    }

    public override ISet<TaintFact> ComputeTargets(TaintFact inFact)
    {
        var outFacts = new HashSet<TaintFact>();

        var (callee, args) = ExtractCall(Edge.From.Operation) ?? (null, Enumerable.Empty<IArgumentOperation>());
        if (callee is null) return outFacts;

        var inv = (Edge.From.Operation as IInvocationOperation)!;

        foreach (var arg in args)
        {
            if (inFact.AppliesTo(arg.Value))
            {
                var param = arg.Parameter;
                outFacts.Add(inFact.WithNewBase(param));
            }
        }

        if (DB.IsSource(callee))
        {
            outFacts.Add(new TaintFact(callee)); // return value tainted
        }

        return outFacts;
    }

    private static (IMethodSymbol callee, IEnumerable<IArgumentOperation> args)? ExtractCall(IOperation op)
    {
        switch (op)
        {
            case IInvocationOperation inv:
                return (inv.TargetMethod, inv.Arguments);
            case IObjectCreationOperation obj:
                return (obj.Constructor, obj.Arguments);
            default:
                // walk children breadth-first until we find a call
                foreach (var child in op.Descendants())
                {
                    if (child is IInvocationOperation cinv)
                        return (cinv.TargetMethod, cinv.Arguments);
                    if (child is IObjectCreationOperation cobj)
                        return (cobj.Constructor, cobj.Arguments);
                }
                return null;
        }
    }
}
