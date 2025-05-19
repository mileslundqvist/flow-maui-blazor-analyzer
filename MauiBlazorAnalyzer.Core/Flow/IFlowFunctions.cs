using MauiBlazorAnalyzer.Core.Flow;

namespace MauiBlazorAnalyzer.Core.Flow;
public interface IFlowFunctions
{
    IFlowFunction GetNormalFlowFunction(ICFGEdge edge);
    IFlowFunction GetCallFlowFunction(ICFGEdge edge);
    IFlowFunction GetReturnFlowFunction(ICFGEdge edge, ICFGNode callSite);
    IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge);
}