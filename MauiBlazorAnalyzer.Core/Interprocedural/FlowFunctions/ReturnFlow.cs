using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class ReturnFlow : BaseFlowFunction
{
    private readonly TaintFact _exitFact;
    private readonly TaintFact _callFact;

    public ReturnFlow(ICFGEdge e, TaintFact exitFact, TaintFact callFact, TaintSpecDB db)
        : base(e, db)
    { _exitFact = exitFact; _callFact = callFact; }

    public override ISet<TaintFact> ComputeTargets(TaintFact inFact)
    {
        var outSet = new HashSet<TaintFact>();
        // Case 1: Exiting fact is already the return value
        if (inFact.IsReturnValue)
        {
            outSet.Add(inFact);
        }
            

        return outSet;
    }
}
