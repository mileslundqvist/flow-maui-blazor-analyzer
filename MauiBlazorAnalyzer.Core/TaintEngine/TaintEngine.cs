using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class TaintEngine
{
    private readonly Compilation _compilation;
    private readonly TaintPropagationVisitor _operationVisitor = new(); // Renamed for clarity

    public TaintEngine(Compilation compilation)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    }

    public async Task AnalyzeProjectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var syntaxTree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            if (semanticModel == null) continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                // Use SymbolFinder or GetDeclaredSymbol safely
                if (semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken) is IMethodSymbol methodSymbol)
                {
                    // Filter for specific method during debugging, remove for full analysis
                    //if (methodSymbol.Name != "DangerousFunction") continue;

                    await AnalyzeMethodInternalAsync(methodSymbol, cancellationToken);
                }
            }
        }
    }

    private async Task AnalyzeMethodInternalAsync(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {

        ControlFlowGraph? cfg = await TryGetControlFlowGraphAsync(methodSymbol, cancellationToken);

        if (cfg == null)
        {
            Console.WriteLine($"Warning: Could not create CFG for method {methodSymbol}. Skipping analysis.");
            return;
        }

        var initialEntryState = CreateInitialAnalysisState(methodSymbol);
        var finalStates = RunFixedPointAnalysis(cfg, initialEntryState);

        PrintAnalysisResults(methodSymbol, finalStates);
    }

    private async Task<ControlFlowGraph?> TryGetControlFlowGraphAsync(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        // Getting the CFG can be tricky. It often requires the syntax node AND a semantic model.
        // The most reliable way is often via an IOperation representing the body, but getting that isn't always direct.
        // Using the syntax node is sometimes a fallback.
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

                    // Fallback/Alternative: Try getting IOperation body first
                    // IOperation? operationBody = semanticModel.GetOperation(syntaxNode, cancellationToken);
                    // if (operationBody is IBlockOperation blockOperation) // Or other relevant operation types
                    // {
                    //     cfg = ControlFlowGraph.Create(blockOperation);
                    //     if (cfg != null) return cfg;
                    // }
                }
                catch (Exception ex)
                {
                    // Log or handle specific exceptions if needed
                    Console.WriteLine($"Error creating CFG for syntax node in {methodSymbol}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"Warning: Failed to create CFG for any syntax reference of method {methodSymbol}.");
        return null;
    }

    private AnalysisState CreateInitialAnalysisState(IMethodSymbol methodSymbol)
    {
        var entryState = AnalysisState.Empty;
        foreach (var param in methodSymbol.Parameters)
        {
            // TODO: Initialize entry state based on method parameters
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
        foreach (var op in block.Operations)
        {
            currentState = op.Accept(_operationVisitor, currentState);
            // The visitor handles state changes based on operations
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
                Console.WriteLine($"Taints in {methodSymbol.Name}:");
                foreach (var stateEntry in kvp.Value.TaintMap)
                {
                    Console.WriteLine($"   Element: {stateEntry.Key} -> Taint: {stateEntry.Value}");
                }
            }
        }
    }
}
