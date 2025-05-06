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
        if (IsZero(inFactAtCallSite)) return Empty; // Should not be called with ZeroFact

        var outFacts = new HashSet<IFact>();

        //if (inFactAtCallSite.IsReturnValue || inFactAtCallSite.Path is null)
        //{
        //    // Return value facts or other non-path facts don't flow into the callee this way.
        //    // If a non-path fact needed to be "global" or context-based, it's a more complex analysis.
        //    return Empty;
        //}


        //var callOp = Edge.From.Operation;
        //if (callOp is null) return outFacts; // Should not happen for CallSite nodes

        //// Find the primary invocation / object creation within the call site operation
        //IMethodSymbol? callee = null;

        //IEnumerable<IArgumentOperation> args = Enumerable.Empty<IArgumentOperation>();
        //IOperation? receiverOperation = null;

        //if (callOp is IInvocationOperation inv)
        //{
        //    callee = inv.TargetMethod;
        //    args = inv.Arguments;
        //    receiverOperation = inv.Instance;
        //}
        //else if (callOp is IObjectCreationOperation obj)
        //{
        //    callee = obj.Constructor;
        //    args = obj.Arguments;
        //    // Object creation doesn't have a receiver operation in the same sense.
        //}
        //else if (callOp is ISimpleAssignmentOperation assign)
        //{
        //    if (assign.Value is IInvocationOperation assignInv)
        //    {
        //        callee = assignInv.TargetMethod;
        //        args = assignInv.Arguments;
        //        receiverOperation = assignInv.Instance;
        //    }
        //    else if (assign.Value is IObjectCreationOperation assignObjCreation)
        //    {
        //        callee = assignObjCreation.Constructor;
        //        args = assignObjCreation.Arguments;
        //    }
        //}
        //// Note: More complex operations wrapping calls would need additional handling
        //// if the node represents them and we need to find the primary call.
        //// Assumes the primary call is the direct IInvocationOperation or the Value of assignment.

        //if (callee is null) return outFacts; // Could not find a callee

        //// Propagate taint from arguments at call site to parameters in callee
        //foreach (var arg in args)
        //{
        //    if (arg.Parameter != null && inFactAtCallSite.AppliesTo(arg.Value))
        //    {
        //        // Create a new fact rooted at the callee's parameter symbol
        //        outFacts.Add(inFactAtCallSite.WithNewBase(arg.Parameter));
        //    }
        //}

        //// Introduce taint if the callee is a source
        //if (DB.IsSource(callee))
        //{
        //    outFacts.Add(new TaintFact(callee));
        //}

        return outFacts;
    }
}
