using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class TaintEngine
{
    private readonly Compilation _compilation;
    private readonly ILogger _logger;
    private readonly TaintPropagationVisitor _operationVisitor = new();

    public TaintEngine(Compilation compilation, ILogger logger)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _logger = logger;
    }

    private static readonly DiagnosticDescriptor TaintSinkRule = new DiagnosticDescriptor(
            id: "MBA003",
            title: "Tainted Data Reaches Sink",
            messageFormat: "Tainted data (potentially from {0}) reaches sensitive sink '{1}' via argument '{2}'",
            category: "Security.Taint",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Data potentially originating from an untrusted source flows into a sensitive operation without proper sanitization.");

    public async Task<ImmutableArray<AnalysisDiagnostic>> AnalyzeProjectAsync(CancellationToken cancellationToken = default)
    {
        var allDiagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

        foreach (var syntaxTree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            if (semanticModel == null) continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDeclaration in methodDeclarations)
            {
                
                // Use SymbolFinder or GetDeclaredSymbol safely
                if (semanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken) is IMethodSymbol methodSymbol)
                {
                    // Filter for specific method during debugging, remove for full analysis
                    if (methodSymbol.Name != "DangerousFunction") continue;

                    var methodDiagnostics = await AnalyzeMethodInternalAsync(methodSymbol, cancellationToken);
                    allDiagnostics.AddRange(methodDiagnostics);
                   
                }
            }
        }

        return allDiagnostics.ToImmutable();
    }

    private async Task<List<AnalysisDiagnostic>> AnalyzeMethodInternalAsync(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        ControlFlowGraph? cfg = await TryGetControlFlowGraphAsync(methodSymbol, cancellationToken);

        if (cfg == null)
        {
            _logger.LogInformation($"Warning: Could not create CFG for method {methodSymbol}. Skipping analysis.");
            return null;
        }

        var initialEntryState = CreateInitialAnalysisState(methodSymbol);
        var finalStates = RunFixedPointAnalysis(cfg, initialEntryState);

        var diagnostics = FindTaintViolations(cfg, finalStates);

        if (diagnostics.Any())
        {
            _logger.LogInformation($"Found {diagnostics.Count} potential tain violations in {methodSymbol.Name}");
        }

        PrintAnalysisResults(methodSymbol, finalStates);

        return diagnostics;


    }

    private List<AnalysisDiagnostic> FindTaintViolations(
        ControlFlowGraph cfg,
        Dictionary<BasicBlock, AnalysisState> finalBlockInputStates)
    {
        var diagnostics = new List<AnalysisDiagnostic>();
        //foreach (var kvp in finalStates)
        //{
        //    if (!kvp.Value.TaintMap.IsEmpty)
        //    {
        //        foreach (var taint in kvp.Value.TaintMap)
        //        {
        //            if (taint.Key is IExpressionStatementOperation expressionStatement)
        //            {
        //                if (expressionStatement.Operation is IInvocationOperation invocation)
        //                {
        //                    var methodSymbol = invocation.TargetMethod;

        //                    if (TaintPolicy.IsSink(methodSymbol.ToDisplayString()))
        //                    {
        //                        foreach (var argument in invocation.Arguments)
        //                        {
        //                            if (argument.Value is ILocalReferenceOperation localRef)
        //                            {
        //                                TaintState tainted = kvp.Value.GetTaint(localRef.Local);

        //                                if (tainted == TaintState.Tainted)
        //                                {
        //                                    var location = argument.Value.Syntax.GetLocation();
        //                                    var diagnostic = Diagnostic.Create(
        //                                            TaintSinkRule,
        //                                            location,
        //                                            "some origin",
        //                                            invocation.TargetMethod.ToDisplayString(),
        //                                            argument.Parameter?.Name ?? $"index {invocation.Arguments.IndexOf(argument)}"
        //                                        );
        //                                    diagnostics.Add(AnalysisDiagnostic.FromRoslynDiagnostic(diagnostic));
        //                                }
        //                            }
        //                        }
        //                    }
        //                }

        //            }
        //        }
        //    }
        //}

        // Problem: Taints propagate to the exit block, which means that if we just check in the current state "finalStates[block]" then we miss the taints which could be in the exit.

        foreach (var block in cfg.Blocks)
        {
            var currentState = finalBlockInputStates.GetValueOrDefault(block, AnalysisState.Empty);


            // Simulate the operations in order within the block
            foreach (var operation in block.Operations)
            {
                if (operation is IExpressionStatementOperation expressionStatement)
                {
                    if (expressionStatement.Operation is IInvocationOperation invocation)
                    {
                        var methodSymbol = invocation.TargetMethod;

                        if (TaintPolicy.IsSink(methodSymbol.ToDisplayString()))
                        {
                            foreach (var argument in invocation.Arguments)
                            {
                                if (argument.Value is ILocalReferenceOperation localRef)
                                {
                                    TaintState tainted = currentState.GetTaint(localRef.Local);

                                    if (tainted == TaintState.Tainted)
                                    {
                                        var location = argument.Value.Syntax.GetLocation();
                                        var diagnostic = Diagnostic.Create(
                                                TaintSinkRule,
                                                location,
                                                "some origin",
                                                invocation.TargetMethod.ToDisplayString(),
                                                localRef.Local.ToDisplayString() ?? $"index {invocation.Arguments.IndexOf(argument)}"
                                            );
                                        diagnostics.Add(AnalysisDiagnostic.FromRoslynDiagnostic(diagnostic));
                                    }
                                }
                            }
                        }
                    }

                }
                currentState = operation.Accept(_operationVisitor, currentState);
            }
        }
        return diagnostics;
    }



    private async Task<ControlFlowGraph?> TryGetControlFlowGraphAsync(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
            var semanticModel = _compilation.GetSemanticModel(syntaxNode.SyntaxTree);

            if (semanticModel != null)
            {
                try
                {
                    // Try creating CFG directly from syntax node
                    var cfg = ControlFlowGraph.Create(syntaxNode, semanticModel);
                    if (cfg != null)
                    {
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    // Log or handle specific exceptions if needed
                    _logger.LogInformation($"Error creating CFG for syntax node in {methodSymbol}: {ex.Message}");
                }
            }
        }

        _logger.LogInformation($"Warning: Failed to create CFG for any syntax reference of method {methodSymbol}.");
        return null;
    }

    private AnalysisState CreateInitialAnalysisState(IMethodSymbol methodSymbol)
    {
        var entryState = AnalysisState.Empty;
        foreach (var param in methodSymbol.Parameters)
        {
            // TODO: Initialize entry state based on method parameters to see if they are tainted
        }
        return entryState;
    }

    private Dictionary<BasicBlock, AnalysisState> RunFixedPointAnalysis(ControlFlowGraph cfg, AnalysisState initialEntryState)
    {
        var blockInputStates = new Dictionary<BasicBlock, AnalysisState>();
        var worklist = new Queue<BasicBlock>();

        if (cfg.Blocks.Length > 0)
        {
            var entryBlock = cfg.Blocks[0]; // Assuming block 0 is always the entry block
            blockInputStates[entryBlock] = initialEntryState;
            worklist.Enqueue(entryBlock);
        }

        while (worklist.Count > 0)
        {
            var currentBlock = worklist.Dequeue();
            var blockInputState = blockInputStates[currentBlock];

            var blockOutputState = ProcessBlock(currentBlock, blockInputState);

            PropagateStateToSuccessors(currentBlock, blockOutputState, blockInputStates, worklist);
        }

        return blockInputStates;
    }

    private AnalysisState ProcessBlock(BasicBlock block, AnalysisState inputState)
    {
        var currentState = inputState;
        foreach (var operation in block.Operations)
        {
            currentState = operation.Accept(_operationVisitor, currentState);
        }

        // Process conditional branch value *after* main operations
        if (block.BranchValue != null)
        {
            currentState = block.BranchValue.Accept(_operationVisitor, currentState);
        }

        return currentState;
    }

    private void PropagateStateToSuccessors(
        BasicBlock currentBlock,
        AnalysisState outputState,
        Dictionary<BasicBlock, AnalysisState> blockInputStates,
        Queue<BasicBlock> worklist)
    {
        ProcessBranch(currentBlock.FallThroughSuccessor);
        ProcessBranch(currentBlock.ConditionalSuccessor);

        void ProcessBranch(ControlFlowBranch? branch)
        {
            if (branch?.Destination != null)
            {
                var successor = branch.Destination;

                // Try to get the existing state; track if it existed.
                bool successorHasExistingState = blockInputStates.TryGetValue(successor, out var existingSuccessorState);
                if (!successorHasExistingState)
                {
                    // If no state existed, the 'previous' state is conceptually Empty.
                    existingSuccessorState = AnalysisState.Empty;
                }

                // Merge the output state from the current block into the successor's state.
                var mergedState = existingSuccessorState.Merge(outputState);

                if (mergedState != existingSuccessorState || !successorHasExistingState)
                {
                    blockInputStates[successor] = mergedState;

                    if (!worklist.Contains(successor))
                    {
                        worklist.Enqueue(successor);
                    }
                }
            }
        }
    }

    private void PrintAnalysisResults(IMethodSymbol methodSymbol, Dictionary<BasicBlock, AnalysisState> finalStates)
    {
        foreach (var kvp in finalStates.OrderBy(kv => kv.Key.Ordinal))
        {
            if (!kvp.Value.TaintMap.IsEmpty)
            {
                _logger.LogInformation($"Taints in {methodSymbol.Name}:");
                foreach (var stateEntry in kvp.Value.TaintMap)
                {
                    _logger.LogInformation($"   Element: {stateEntry.Key} -> Taint: {stateEntry.Value}");
                }
            }
        }
    }
}
