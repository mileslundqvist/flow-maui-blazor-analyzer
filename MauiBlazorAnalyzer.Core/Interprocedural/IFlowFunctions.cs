namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFlowFunctions
{
    IFlowFunction GetNormalFlowFunction(ICFGEdge edge);
    IFlowFunction GetCallFlowFunction(ICFGEdge edge);
    IFlowFunction GetReturnFlowFunction(ICFGEdge edge, ICFGNode callSite);
    IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge);
}