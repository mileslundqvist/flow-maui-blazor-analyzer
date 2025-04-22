using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public sealed class TaintFlowFunctions : IFlowFunctions
{
    private readonly TaintSpecDB _db = TaintSpecDB.Instance;

    public IFlowFunction GetCallFlowFunction(ICFGEdge edge, TaintFact inFact)
        => new CallFlow(edge, _db);

    public IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge, TaintFact inFact)
        => new CallToReturnFlow(edge, _db);

    public IFlowFunction GetNormalFlowFunction(ICFGEdge edge, TaintFact inFact)
        => new NormalFlow(edge, _db);

    public IFlowFunction GetReturnFlowFunction(ICFGEdge edge, TaintFact exitFact, TaintFact callsiteFact)
        => new ReturnFlow(edge, exitFact, callsiteFact, _db);
}
