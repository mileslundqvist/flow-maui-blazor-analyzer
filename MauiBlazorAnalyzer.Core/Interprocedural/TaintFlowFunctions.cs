using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public sealed class TaintFlowFunctions : IFlowFunctions
{
    private readonly TaintSpecDB _db = TaintSpecDB.Instance;
    private readonly List<EntryPointInfo> _entryPoints;
    public TaintFlowFunctions(IEnumerable<EntryPointInfo> entryPoints)
    {
        _entryPoints = entryPoints.ToList();
    }

    public IFlowFunction GetCallFlowFunction(ICFGEdge edge)
        => new CallFlow(edge, _db, _entryPoints);

    public IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge)
        => new CallToReturnFlow(edge, _db, _entryPoints);

    public IFlowFunction GetNormalFlowFunction(ICFGEdge edge)
        => new NormalFlow(edge, _db, _entryPoints);

    public IFlowFunction GetReturnFlowFunction(ICFGEdge edge, ICFGNode callSite)
        => new ReturnFlow(edge, callSite, _db, _entryPoints);
}
