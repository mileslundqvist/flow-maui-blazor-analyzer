using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class ReturnFlow : BaseFlowFunction
{
    private readonly ICFGNode _callSite;

    public ReturnFlow(ICFGEdge e, ICFGNode callSite, TaintSpecDB db, List<EntryPointInfo> entryPoints)
        : base(e, db, entryPoints)
    {
        _callSite = callSite;
    }

    public override ISet<IFact> ComputeTargets(IFact inFact)
    {

        var outSet = new HashSet<IFact>();

        // Case 1: Exiting fact is the return value
        if (_callSite.Operation is ISimpleAssignmentOperation assign)
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
        }
        return outSet;
    }
}
