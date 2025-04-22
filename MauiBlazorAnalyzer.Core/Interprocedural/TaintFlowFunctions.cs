using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public sealed class TaintFlowFunctions : IFlowFunctions
{
    private readonly TaintSpecDB _db = TaintSpecDB.Instance;

    public IFlowFunction GetCallFlowFunction(ICFGEdge edge)
        => new CallFlow(edge, _db);

    public IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge)
        => new CallToReturnFlow(edge, _db);

    public IFlowFunction GetNormalFlowFunction(ICFGEdge edge)
        => new NormalFlow(edge, _db);

    public IFlowFunction GetReturnFlowFunction(ICFGEdge edge, ICFGNode callSite)
        => new ReturnFlow(edge, callSite, _db);
}
