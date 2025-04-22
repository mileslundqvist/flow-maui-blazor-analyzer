using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

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
        if (IsZero(_exitFact)) return Empty; // Should not be called with ZeroFact

        var outSet = new HashSet<TaintFact>();
        var returnSiteOp = Edge.To.Operation;

        // Case 1: Exiting fact is the return value
        if (_exitFact.IsReturnValue)
        {
            if (returnSiteOp is ISimpleAssignmentOperation assign)
            {
                var destinationSymbol = assign.Target switch
                {
                    ILocalReferenceOperation l => l.Local as ISymbol,
                    IParameterReferenceOperation p => p.Parameter, // Should not happen if parameter is passed by value, but possible for ref/out?
                    IFieldReferenceOperation f => f.Field,
                    IPropertyReferenceOperation pr => pr.Property,
                    _ => null
                };

                if (destinationSymbol is not null)
                {
                    // Create a new fact rooted at the assignment target.
                    outSet.Add(new TaintFact(new AccessPath(destinationSymbol, ImmutableArray<IFieldSymbol>.Empty)));
                }
                outSet.Add(_exitFact);
            }
            else
            {
                outSet.Add(_exitFact);
            }
        }

        // Case 2: Handling facts related to parameters or fields modified within the callee


        return outSet;
    }
}
