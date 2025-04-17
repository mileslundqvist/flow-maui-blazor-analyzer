namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFlowFunctions
{
    IFlowFunction GetNormalFlowFunction(ICFGEdge edge, TaintFact sourceFact);
    IFlowFunction GetCallFlowFunction(ICFGEdge edge, TaintFact sourceFact);
    IFlowFunction GetReturnFlowFunction(ICFGEdge edge, TaintFact exitFact, TaintFact callsiteFact);
    IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge, TaintFact sourceFact);
}