using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class TaintFlowFunctions : IFlowFunctions
{
    private static readonly IFlowFunction Identity = new LambdaFlowFunction(fact => new HashSet<TaintFact> { fact });
    private static readonly IFlowFunction KillAll = new LambdaFlowFunction(fact => new HashSet<TaintFact>());

    public IFlowFunction GetCallFlowFunction(ICFGEdge edge, TaintFact sourceFact)
    {
        var operation = edge.From.Operation;

        if (operation is IAssignmentOperation assignment)
        {
            
        }

        return Identity;
    }

    public IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge, TaintFact sourceFact)
    {
        throw new NotImplementedException();
    }

    public IFlowFunction GetNormalFlowFunction(ICFGEdge edge, TaintFact sourceFact)
    {
        throw new NotImplementedException();
    }

    public IFlowFunction GetReturnFlowFunction(ICFGEdge edge, TaintFact exitFact, TaintFact callsiteFact)
    {
        throw new NotImplementedException();
    }
}
