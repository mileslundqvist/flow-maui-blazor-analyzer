using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class TaintAnalysisProblem : IFDSTabulationProblem
{
    private readonly InterproceduralCFG _graph;
    private readonly IFlowFunctions _flowFunctions;
    private readonly ZeroFact _zeroValue = ZeroFact.Instance;

    public InterproceduralCFG Graph => _graph;
    public IFlowFunctions FlowFunctions => _flowFunctions;
    public IReadOnlyDictionary<ICFGNode, ISet<IFact>> InitialSeeds { get; }
    public ZeroFact ZeroValue => _zeroValue;


    public TaintAnalysisProblem(Compilation compilation, IEnumerable<ICFGNode> roots)
    {
        _graph = new InterproceduralCFG(compilation, roots);
        _flowFunctions = new TaintFlowFunctions();
        InitialSeeds = ComputeInitialSeeds(_graph);
    }

    private IReadOnlyDictionary<ICFGNode, ISet<IFact>> ComputeInitialSeeds(InterproceduralCFG graph)
    {
        Dictionary<ICFGNode, HashSet<IFact>> initialSeeds = new();

        foreach (var node in _graph.EntryNodes)
        {
            var entryFacts = new HashSet<IFact>();
            if (node.MethodContext.MethodSymbol.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == "Microsoft.JSInterop.JSInvokableAttribute"))
            {
                var taintFact = new TaintFact(node.MethodContext.MethodSymbol);
                entryFacts.Add(taintFact);
            }
            else
            {
                entryFacts.Add(_zeroValue);
            }
            initialSeeds.Add(node, entryFacts);
        }

        return (IReadOnlyDictionary<ICFGNode, ISet<IFact>>)initialSeeds;
    }
}
