using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class TaintAnalysisProblem : IFDSTabulationProblem
{
    private readonly InterproceduralCFG _graph;
    private readonly IFlowFunctions _flowFunctions;
    private readonly IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> _initialSeeds;
    private readonly ZeroFact _zeroValue = ZeroFact.Instance;

    public InterproceduralCFG Graph => _graph;
    public IFlowFunctions FlowFunctions => _flowFunctions;
    public IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> InitialSeeds => _initialSeeds;
    public ZeroFact ZeroValue => _zeroValue;


    public TaintAnalysisProblem(Compilation compilation, IEnumerable<ICFGNode> initialEntryNodes)
    {
        _graph = new InterproceduralCFG(compilation, initialEntryNodes);
        _flowFunctions = new TaintFlowFunctions();
        _initialSeeds = ComputeInitialSeeds(_graph);
    }

    private IReadOnlyDictionary<ICFGNode, ISet<TaintFact>> ComputeInitialSeeds(InterproceduralCFG graph)
    {
        var seeds = new Dictionary<ICFGNode, ISet<TaintFact>>();

        foreach (ICFGNode node in _graph.Nodes)
        {
            var factSet = new HashSet<TaintFact>();
            seeds.Add(node, factSet);
        }

        return seeds;
    }
}
