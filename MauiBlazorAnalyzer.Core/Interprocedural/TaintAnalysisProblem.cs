using MauiBlazorAnalyzer.Core.EntryPoints;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

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


    public TaintAnalysisProblem(Project project, Compilation compilation, IEnumerable<EntryPointInfo> entryPoints)
    {
        ArgumentNullException.ThrowIfNull(project, nameof(project));
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(entryPoints);

        var rootMethodSymbols = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var entryPoint in entryPoints)
        {
            if (entryPoint.EntryPointSymbol is IMethodSymbol methodSymbol)
            {
                rootMethodSymbols.Add(methodSymbol);
            } 
            else if (entryPoint.EntryPointSymbol is IFieldSymbol fieldSymbol)
            {
                if (fieldSymbol.ContainingSymbol is IMethodSymbol containingMethod)
                {
                    rootMethodSymbols.Add(containingMethod);
                }
            }
        }

        // Create the ICFG using the identified root methods
        _graph = new InterproceduralCFG(project, compilation, rootMethodSymbols);

        // Set up flow functions
        _flowFunctions = new TaintFlowFunctions();

        // Compute initial seeds based on EntryPointInfo and the created graph
        InitialSeeds = ComputeInitialSeeds(entryPoints, _graph);
    }

    private IReadOnlyDictionary<ICFGNode, ISet<IFact>> ComputeInitialSeeds(
        IEnumerable<EntryPointInfo> entryPoints,
        InterproceduralCFG graph
        )
    {
        var initialSeeds = new Dictionary<ICFGNode, ISet<IFact>>();

        foreach (var entryPoint in entryPoints)
        {
            IMethodSymbol? targetMethodSymbol = entryPoint.EntryPointSymbol as IMethodSymbol;

            if (targetMethodSymbol == null && entryPoint.EntryPointSymbol is IFieldSymbol fieldSymbol)
            {
                targetMethodSymbol = fieldSymbol.ContainingSymbol as IMethodSymbol;
            }

            if (targetMethodSymbol == null)
                continue;

            if (!graph.TryGetEntryNode(targetMethodSymbol, out var entryNode))
            {
                continue;
            }

            // Ensure the node has an entry in the dictionary
            if (!initialSeeds.TryGetValue(entryNode, out var entryFacts))
            {
                entryFacts = new HashSet<IFact> { _zeroValue };
                initialSeeds[entryNode] = entryFacts;
            }

            switch (entryPoint.Type)
            {
                case EntryPointType.JSInvokableMethod:
                    if (entryPoint.MethodSymbol != null)
                    {
                        foreach (var parameter in entryPoint.TaintedParameters)
                        {
                            if (SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, entryPoint.MethodSymbol))
                            {
                                var path = new AccessPath(parameter, ImmutableArray<IFieldSymbol>.Empty);
                                var fact = new TaintFact(path);
                                if (entryFacts.Add(fact))
                                {

                                }
                            }
                        }
                    }
                    break;
                case EntryPointType.ParameterSetter:
                case EntryPointType.LifecycleMethod:
                case EntryPointType.EventHandlerMethod:
                case EntryPointType.BindingSetter:
                    break; 

                default:
                    Console.Error.WriteLine($"Warning: Unhandled EntryPointType '{entryPoint.Type}' during initial seed computation.");
                    break;
            }
        }

        foreach (var node in graph.EntryNodes)
        {
            if (!initialSeeds.ContainsKey(node))
            {
                initialSeeds[node] = new HashSet<IFact> { _zeroValue };
            }
        }

        return initialSeeds;
    }
}
