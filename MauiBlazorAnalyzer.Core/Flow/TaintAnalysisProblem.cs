using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Flow;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;

namespace MauiBlazorAnalyzer.Core.Flow;
public class TaintAnalysisProblem : IFDSTabulationProblem
{
    private readonly ILogger<TaintAnalysisProblem> _logger;
    private readonly InterproceduralCFG _graph;
    private readonly IFlowFunctions _flowFunctions;
    private readonly ZeroFact _zeroValue = ZeroFact.Instance;
    private readonly IReadOnlyList<EntryPointInfo> _entryPointsInfo;
    private bool _disposed;

    public InterproceduralCFG Graph => _graph;
    public IFlowFunctions FlowFunctions => _flowFunctions;
    public IReadOnlyDictionary<ICFGNode, ISet<IFact>> InitialSeeds { get; }
    public ZeroFact ZeroValue => _zeroValue;


    public TaintAnalysisProblem(
        Project project, 
        Compilation compilation, 
        IEnumerable<EntryPointInfo> entryPoints,
        ILogger<TaintAnalysisProblem>? logger = null,
        CancellationToken cancellationToken = default)
    {
        _logger = logger ?? NullLogger<TaintAnalysisProblem>.Instance;

        ValidateParameters(project, compilation, entryPoints);

        try
        {
            _entryPointsInfo = ProcessEntryPoints(entryPoints, cancellationToken);
            var rootMethodSymbols = ExtractRootMethodSymbols(_entryPointsInfo);

            _graph = CreateInterproceduralCFG(project, compilation, rootMethodSymbols, cancellationToken);
            _flowFunctions = CreateFlowFunctions(_entryPointsInfo);
            InitialSeeds = ComputeInitialSeeds(_entryPointsInfo, _graph);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize taint analysis problem");
            throw;
        }

    }

    private static void ValidateParameters(Project project, Compilation compilation, IEnumerable<EntryPointInfo> entryPoints)
    {
        ArgumentNullException.ThrowIfNull(project, nameof(project));
        ArgumentNullException.ThrowIfNull(compilation, nameof(compilation));
        ArgumentNullException.ThrowIfNull(entryPoints, nameof(entryPoints));
    }


    private IReadOnlyList<EntryPointInfo> ProcessEntryPoints(IEnumerable<EntryPointInfo> entryPoints, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entryPointsList = entryPoints.ToList();

        if (entryPointsList.Count == 0)
        {
            _logger.LogWarning("No entry points provided for taint analysis");
            return entryPointsList;
        }

        _logger.LogDebug("Processing {Count} entry points", entryPointsList.Count);

        // Validate entry points
        var validEntryPoints = new List<EntryPointInfo>();
        var invalidCount = 0;

        foreach (var entryPoint in entryPointsList)
        {
            if (IsValidEntryPoint(entryPoint))
            {
                validEntryPoints.Add(entryPoint);
            }
            else
            {
                invalidCount++;
                _logger.LogWarning("Invalid entry point detected: {EntryPoint}", entryPoint);
            }
        }

        if (invalidCount > 0)
        {
            _logger.LogWarning("Filtered out {InvalidCount} invalid entry points", invalidCount);
        }

        _logger.LogInformation("Processed entry points: {Valid} valid, {Invalid} invalid",
            validEntryPoints.Count, invalidCount);

        return validEntryPoints;
    }

    private static bool IsValidEntryPoint(EntryPointInfo entryPoint)
    {
        return entryPoint != null &&
               entryPoint.Type != default &&
               (entryPoint.MethodSymbol != null || entryPoint.PropertySymbol != null);
    }

    private HashSet<IMethodSymbol> ExtractRootMethodSymbols(IReadOnlyList<EntryPointInfo> entryPoints)
    {
        _logger.LogDebug("Extracting root method symbols from {Count} entry points", entryPoints.Count);

        var rootMethodSymbols = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var processedCount = 0;
        var skippedCount = 0;

        foreach (var entryPoint in entryPoints)
        {
            var methodSymbol = GetMethodSymbolFromEntryPoint(entryPoint);

            if (methodSymbol != null)
            {
                var originalDefinition = methodSymbol.OriginalDefinition;
                if (rootMethodSymbols.Add(originalDefinition))
                {
                    _logger.LogTrace("Added root method: {Method}", originalDefinition.ToDisplayString());
                    processedCount++;
                }
                else
                {
                    _logger.LogTrace("Method already added: {Method}", originalDefinition.ToDisplayString());
                }
            }
            else
            {
                skippedCount++;
                _logger.LogDebug("Skipped entry point without method symbol: {EntryPoint}", entryPoint);
            }
        }

        if (rootMethodSymbols.Count == 0)
        {
            _logger.LogWarning("No root methods identified for ICFG construction from {Count} entry points", entryPoints.Count);
        }
        else
        {
            _logger.LogInformation("Extracted {RootMethodCount} unique root methods ({ProcessedCount} processed, {SkippedCount} skipped)",
                rootMethodSymbols.Count, processedCount, skippedCount);
        }

        return rootMethodSymbols;
    }

    private static IMethodSymbol? GetMethodSymbolFromEntryPoint(EntryPointInfo entryPoint)
    {
        return entryPoint.Type switch
        {
            EntryPointType.JSInvokableMethod or
            EntryPointType.LifecycleMethod or
            EntryPointType.EventHandlerMethod or
            EntryPointType.ParameterSetter => entryPoint.MethodSymbol,
            _ => null
        };
    }

    private InterproceduralCFG CreateInterproceduralCFG(
        Project project,
        Compilation compilation,
        HashSet<IMethodSymbol> rootMethodSymbols,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating interprocedural CFG with {Count} root methods", rootMethodSymbols.Count);

        try
        {
            var cfg = new InterproceduralCFG(project, compilation, rootMethodSymbols, cancellationToken);
            _logger.LogDebug("Successfully created interprocedural CFG");
            return cfg;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create interprocedural CFG");
            throw;
        }
    }

    private static IFlowFunctions CreateFlowFunctions(IReadOnlyList<EntryPointInfo> entryPoints)
    {
        return new TaintFlowFunctions(entryPoints);
    }

    private IReadOnlyDictionary<ICFGNode, ISet<IFact>> ComputeInitialSeeds(
        IReadOnlyList<EntryPointInfo> entryPoints,
        InterproceduralCFG graph)
    {
        _logger.LogDebug("Computing initial seeds for {Count} entry points", entryPoints.Count);

        var initialSeeds = new Dictionary<ICFGNode, ISet<IFact>>();
        var taintedSeedCount = 0;

        try
        {
            // Process entry points that introduce taint
            foreach (var entryPoint in entryPoints)
            {
                var seedsAdded = ProcessEntryPointSeeds(entryPoint, graph, initialSeeds);
                taintedSeedCount += seedsAdded;
            }

            // Ensure all entry nodes have at least ZeroValue
            var entryNodes = graph.Nodes.Where(n => n.Kind == ICFGNodeKind.Entry).ToList();
            var zeroSeedsAdded = EnsureZeroSeeds(entryNodes, initialSeeds);

            _logger.LogInformation(
                "Computed initial seeds: {TaintedSeeds} tainted seeds, {ZeroSeeds} zero seeds added, {TotalNodes} total entry nodes, {TotalSeeds} total seed facts",
                taintedSeedCount,
                zeroSeedsAdded,
                entryNodes.Count,
                initialSeeds.Sum(kvp => kvp.Value.Count));

            return initialSeeds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing initial seeds");
            throw;
        }
    }

    private int ProcessEntryPointSeeds(
        EntryPointInfo entryPoint,
        InterproceduralCFG graph,
        Dictionary<ICFGNode, ISet<IFact>> initialSeeds)
    {
        var methodSymbol = GetMethodSymbolFromEntryPoint(entryPoint);
        if (methodSymbol == null)
        {
            _logger.LogTrace("No method symbol for entry point: {EntryPoint}", entryPoint);
            return 0;
        }

        if (!graph.TryGetEntryNode(methodSymbol.OriginalDefinition, out var entryNode))
        {
            _logger.LogDebug("Could not find entry node for method: {Method}", methodSymbol.ToDisplayString());
            return 0;
        }

        // Ensure the node has an entry with at least ZeroValue
        if (!initialSeeds.TryGetValue(entryNode, out var entryFacts))
        {
            entryFacts = new HashSet<IFact> { _zeroValue };
            initialSeeds[entryNode] = entryFacts;
        }

        return ProcessEntryPointTaintSeeds(entryPoint, entryFacts, methodSymbol);
    }

    private int ProcessEntryPointTaintSeeds(
        EntryPointInfo entryPoint,
        ISet<IFact> entryFacts,
        IMethodSymbol methodSymbol)
    {
        var seedsAdded = 0;

        try
        {
            switch (entryPoint.Type)
            {
                case EntryPointType.JSInvokableMethod:
                    seedsAdded = ProcessJSInvokableSeeds(entryPoint, entryFacts, methodSymbol);
                    break;

                case EntryPointType.ParameterSetter:
                case EntryPointType.BindingCallback:
                case EntryPointType.LifecycleMethod:
                case EntryPointType.EventHandlerMethod:
                    // These are handled by flow functions, not initial seeds
                    _logger.LogTrace("Entry point {Type} handled by flow functions, not initial seeds", entryPoint.Type);
                    break;

                default:
                    _logger.LogWarning("Unknown entry point type: {Type}", entryPoint.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing taint seeds for entry point {Type}", entryPoint.Type);
        }

        return seedsAdded;
    }

    private int ProcessJSInvokableSeeds(
        EntryPointInfo entryPoint,
        ISet<IFact> entryFacts,
        IMethodSymbol methodSymbol)
    {
        if (!entryPoint.TaintedParameters.Any())
        {
            _logger.LogTrace("No tainted parameters for JSInvokable method: {Method}", methodSymbol.Name);
            return 0;
        }

        var seedsAdded = 0;

        foreach (var parameter in entryPoint.TaintedParameters)
        {
            try
            {
                var path = new AccessPath(parameter, ImmutableArray<IFieldSymbol>.Empty);
                var fact = new TaintFact(path);

                if (entryFacts.Add(fact))
                {
                    seedsAdded++;
                    _logger.LogTrace("Added taint seed for parameter {Parameter} in method {Method}",
                        parameter.Name, methodSymbol.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating taint fact for parameter {Parameter}", parameter.Name);
            }
        }

        if (seedsAdded > 0)
        {
            _logger.LogDebug("Added {Count} taint seeds for JSInvokable method {Method}",
                seedsAdded, methodSymbol.Name);
        }

        return seedsAdded;
    }

    private int EnsureZeroSeeds(IList<ICFGNode> entryNodes, Dictionary<ICFGNode, ISet<IFact>> initialSeeds)
    {
        var zeroSeedsAdded = 0;

        foreach (var entryNode in entryNodes)
        {
            if (!initialSeeds.ContainsKey(entryNode))
            {
                initialSeeds[entryNode] = new HashSet<IFact> { _zeroValue };
                zeroSeedsAdded++;
                _logger.LogTrace("Added zero seed for entry node: {Node}", entryNode);
            }
        }

        if (zeroSeedsAdded > 0)
        {
            _logger.LogDebug("Added zero seeds to {Count} entry nodes", zeroSeedsAdded);
        }

        return zeroSeedsAdded;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _logger.LogDebug("Disposing TaintAnalysisProblem");

            // If InterproceduralCFG implements IDisposable, dispose it
            // (Currently it doesn't, but this is future-proofing)
            if (_graph is IDisposable disposableCfg)
            {
                disposableCfg.Dispose();
            }

            // If FlowFunctions implements IDisposable, dispose it
            if (_flowFunctions is IDisposable disposableFlowFunctions)
            {
                disposableFlowFunctions.Dispose();
            }

            _disposed = true;
        }
    }
}
