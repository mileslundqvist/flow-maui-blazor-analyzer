using MauiBlazorAnalyzer.Core.EntryPoints;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class TaintAnalysisProblem : IFDSTabulationProblem
{
    private readonly InterproceduralCFG _graph;
    private readonly IFlowFunctions _flowFunctions;
    private readonly ZeroFact _zeroValue = ZeroFact.Instance;
    private readonly IEnumerable<EntryPointInfo> _entryPointsInfo; // Store for Flow Functions

    public InterproceduralCFG Graph => _graph;
    public IFlowFunctions FlowFunctions => _flowFunctions;
    public IReadOnlyDictionary<ICFGNode, ISet<IFact>> InitialSeeds { get; }
    public ZeroFact ZeroValue => _zeroValue;


    public TaintAnalysisProblem(Project project, Compilation compilation, IEnumerable<EntryPointInfo> entryPoints)
    {
        ArgumentNullException.ThrowIfNull(project, nameof(project));
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(entryPoints);

        _entryPointsInfo = entryPoints.ToList();

        var rootMethodSymbols = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var entryPoint in _entryPointsInfo)
        {
            IMethodSymbol? methodToAdd = null;
            if (entryPoint.EntryPointSymbol is IMethodSymbol methodSymbol)
            {
                methodToAdd = methodSymbol;
            }
            if (methodToAdd != null)
            {
                // Use OriginalDefinition to handle generics consistently
                rootMethodSymbols.Add(methodToAdd.OriginalDefinition);
            }

            if (!rootMethodSymbols.Any())
            {
                // Handle case with no entry points found or consider adding default roots (e.g., Main)
                Console.Error.WriteLine("Warning: No root methods identified for ICFG construction based on provided entry points.");
                // Potentially add Program.Main or other fallbacks if applicable
            }

        }

        _graph = new InterproceduralCFG(project, compilation, rootMethodSymbols);

        // --- Pass EntryPointInfo to Flow Functions ---
        _flowFunctions = new TaintFlowFunctions(); // Modify constructor

        // Compute initial seeds (simplified)
        InitialSeeds = ComputeInitialSeeds(_entryPointsInfo, _graph);
    }

    private IReadOnlyDictionary<ICFGNode, ISet<IFact>> ComputeInitialSeeds(
        IEnumerable<EntryPointInfo> entryPoints,
        InterproceduralCFG graph)
    {
        var initialSeeds = new Dictionary<ICFGNode, ISet<IFact>>();

        foreach (var entryPoint in entryPoints)
        {
            IMethodSymbol? targetMethodSymbol = null;

            // Get the method symbol associated with the entry point trigger
            if (entryPoint.Type == EntryPointType.JSInvokableMethod ||
                entryPoint.Type == EntryPointType.LifecycleMethod ||
                entryPoint.Type == EntryPointType.EventHandlerMethod ||
                entryPoint.Type == EntryPointType.ParameterSetter) // Setters are methods
            {
                targetMethodSymbol = entryPoint.MethodSymbol;
            }
            // --- REMOVED LOGIC FOR BindingCallbackParameter here ---
            // We don't seed based on the lambda parameter directly anymore.

            if (targetMethodSymbol == null) continue;

            // Try to find the entry node in the graph for the *triggering* method
            if (!graph.TryGetEntryNode(targetMethodSymbol.OriginalDefinition, out var entryNode))
            {
                // Console.WriteLine($"Debug: Could not find entry node for method: {targetMethodSymbol.ToDisplayString()}");
                continue;
            }

            // Ensure the node has an entry in the dictionary, always include ZeroValue
            if (!initialSeeds.TryGetValue(entryNode, out var entryFacts))
            {
                entryFacts = new HashSet<IFact> { _zeroValue };
                initialSeeds[entryNode] = entryFacts;
            }

            // --- Handle only entry points that directly taint parameters on entry ---
            switch (entryPoint.Type)
            {
                case EntryPointType.JSInvokableMethod:
                    if (entryPoint.MethodSymbol != null && entryPoint.TaintedParameters.Any())
                    {
                        foreach (var parameter in entryPoint.TaintedParameters)
                        {
                            // Create a fact representing the tainted parameter
                            // Assuming TaintFact takes an AccessPath or similar
                            var path = new AccessPath(parameter, ImmutableArray<IFieldSymbol>.Empty); // Example AccessPath
                            var fact = new TaintFact(path); // Example TaintFact
                            entryFacts.Add(fact);
                            // Console.WriteLine($"Debug: Seeding TaintFact for {parameter.Name} at entry of {targetMethodSymbol.Name}");
                        }
                    }
                    break;

                // --- Cases that don't directly taint parameters on entry ---
                case EntryPointType.ParameterSetter:
                    // Taint comes from the assignment *within* the setter, handled by flow functions.
                    break;
                case EntryPointType.BindingCallbackParameter:
                    // Taint introduced *within* the lambda body, handled by flow functions using EntryPointInfo.
                    break;
                case EntryPointType.LifecycleMethod:
                case EntryPointType.EventHandlerMethod:
                    // These methods are entry points for execution, but taint typically
                    // comes from reading tainted state (fields) or specific event args,
                    // handled by flow functions.
                    break;

                default:
                    // Should not happen if all types are handled
                    break;
            }
        }

        // Ensure *all* method entry nodes in the graph have at least the ZeroValue seeded
        foreach (var node in graph.Nodes.Where(n => n.Kind == ICFGNodeKind.Entry)) // Adjust based on your ICFGNode structure
        {
            if (!initialSeeds.ContainsKey(node))
            {
                initialSeeds[node] = new HashSet<IFact> { _zeroValue };
            }
        }


        return initialSeeds;
    }
}
