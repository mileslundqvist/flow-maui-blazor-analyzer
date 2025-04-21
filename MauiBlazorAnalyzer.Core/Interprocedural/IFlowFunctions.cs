namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IFlowFunctions
{
    IFlowFunction GetNormalFlowFunction(ICFGEdge edge, TaintFact inFact);
    IFlowFunction GetCallFlowFunction(ICFGEdge edge, TaintFact inFact);
    IFlowFunction GetReturnFlowFunction(ICFGEdge edge, TaintFact exitFact, TaintFact callsiteFact);
    IFlowFunction GetCallToReturnFlowFunction(ICFGEdge edge, TaintFact inFact);
}