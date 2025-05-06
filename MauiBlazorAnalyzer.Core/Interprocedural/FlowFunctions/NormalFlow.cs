using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class NormalFlow : BaseFlowFunction
{
    public NormalFlow(ICFGEdge edge, TaintSpecDB db, List<EntryPointInfo> entryPoints) : base(edge, db, entryPoints) { }

    public override ISet<IFact> ComputeTargets(IFact inFact)
    {
        // Now we need to handle the IFact, because a Taint can be introduced from a source or by being a bind-variable
        var outFacts = new HashSet<IFact>();
        var operation = Edge.From.Operation;
        var containingMethod = Edge.From.MethodContext.MethodSymbol;

        bool handledAsBinding = false;


        if (operation is ISimpleAssignmentOperation assignmentOp 
            && containingMethod is not null)
        {
            foreach (var entryPoint in EntryPoints)
            {
                if (entryPoint.Type != EntryPointType.BindingCallback || entryPoint.EntryPointSymbol == null || entryPoint.AssociatedSymbol == null)
                    continue;

                
            }
        }


        
        return outFacts;
    }
}
