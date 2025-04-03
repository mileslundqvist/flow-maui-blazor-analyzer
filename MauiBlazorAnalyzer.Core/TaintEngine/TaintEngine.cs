using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class TaintEngine
{
    private readonly Compilation _compilation;
    private readonly TaintPropagationVisitor _visitor = new();

    public TaintEngine(Compilation compilation)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    }

    public async Task AnalyzeProjectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var syntaxTree in _compilation.SyntaxTrees)
        {
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();


            // Loop through all declared methods
            foreach (var methodDecl in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (methodSymbol != null && methodSymbol is IMethodSymbol)
                {
                    await AnalyzeMethodAsync((IMethodSymbol)methodSymbol, semanticModel, cancellationToken);
                }
            }
        }
    }

    private async Task AnalyzeMethodAsync(IMethodSymbol methodSymbol, SemanticModel initialModel, CancellationToken cancellationToken)
    {
        
        if (methodSymbol.Name != "DangerousFunction") return;

        Console.WriteLine($"Analyzing method: {methodSymbol}");
        // Find the operation corresponding to the method body
        IOperation? operationBody = null;
        foreach (var syntaxRef in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
            var semanticModel = _compilation.GetSemanticModel(syntaxNode.SyntaxTree);
            operationBody = semanticModel.GetOperation(syntaxNode, cancellationToken);
            if (operationBody != null) break;
        }


        if (operationBody == null)
        {
            Console.WriteLine($"Warning: Could not get IOperation for method {methodSymbol}. Skipping.");
            return;
        }

        // --- Get Control Flow Graph ---
        ControlFlowGraph? cfg = null;
        try
        {
            var syntaxForCfg = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
            if (syntaxForCfg != null)
            {
                cfg = ControlFlowGraph.Create(syntaxForCfg, initialModel);
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating CFG for {methodSymbol}: {ex.Message}");
        }


        if (cfg == null)
        {
            Console.WriteLine($"Warning: Could not create CFG for method {methodSymbol}. Skipping intra-procedural analysis.");
            return;
        }

        // --- Fixed-Point Iteration Setup ---
        var blockInputStates = new Dictionary<BasicBlock, AnalysisState>();

        // Worklist of blocks to process
        var worklist = new Queue<BasicBlock>();

        var entryState = AnalysisState.Empty;
        foreach (var param in methodSymbol.Parameters)
        {
            // TODO: Handle real parameter types
            if (param.Type.Name.Contains("HttpRequest"))
            {
                entryState = entryState.SetTaint(param, TaintState.Tainted);
            }
        }

        var entryBlock = cfg.Blocks[0];

        blockInputStates[entryBlock] = entryState;
        worklist.Enqueue(entryBlock);

        // --- Run Analysis Loop ---
        while (worklist.Count > 0)
        {
            var currentBlock = worklist.Dequeue();
            var currentState = blockInputStates[currentBlock];

            // Process operations in the block
            foreach (var op in currentBlock.Operations)
            {
                currentState = op.Accept(_visitor, currentState);
                // TODO: Handle results of operations that affect subsequent operations (e.g., return values)
            }

            // Process conditional branch value if it exists
            if (currentBlock.BranchValue != null)
            {
                currentState = currentBlock.BranchValue.Accept(_visitor, currentState);
            }

            var outputState = currentState;

            // Propagate state to successors
            void Propagate(ControlFlowBranch? branch)
            {
                if (branch?.Destination != null)
                {
                    var successor = branch.Destination;
                    var existingSuccessorState = blockInputStates.GetValueOrDefault(successor, AnalysisState.Empty);
                    var mergedState = existingSuccessorState.Merge(outputState);

  
                    blockInputStates[successor] = mergedState;
                    if (!worklist.Contains(successor))
                    {
                        worklist.Enqueue(successor);
                    }
                    
                }
            }

            Propagate(currentBlock.FallThroughSuccessor);
            Propagate(currentBlock.ConditionalSuccessor);
        }

        Console.WriteLine($"Finished analyzing method: {methodSymbol}");
        // TODO: Store/report results from blockInputStates

        foreach (var kvp in blockInputStates)
        {
            if (kvp.Value.TaintMap.Count > 0)
            {
                Console.WriteLine($"Block: {kvp.Key}");
                foreach (var state in kvp.Value.TaintMap)
                {
                    Console.WriteLine($"  {state.Key}: {state.Value}");
                }
            }
            else
            {
                Console.WriteLine($"Block: {kvp.Key} - No taint detected.");
            }
        }
    }
}
