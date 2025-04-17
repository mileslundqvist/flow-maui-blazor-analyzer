
namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class LambdaFlowFunction : IFlowFunction
{
    private readonly Func<TaintFact, ISet<TaintFact>> _func;
    public LambdaFlowFunction(Func<TaintFact, ISet<TaintFact>> func)
    {
        _func = func;
    }

    public ISet<TaintFact> ComputeTargets(TaintFact sourceFact) => _func(sourceFact);
}
