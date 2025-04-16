using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Intraprocedural.Context;
public class MethodAnalysisContext
{
    public readonly IMethodSymbol MethodSymbol;
    public readonly IOperation Operation;
    //MethodSummary? _methodSummary; TODO: Add this when summaries are implemented.

    ControlFlowGraph? _controlFlowGraph;
    private bool _isControlFlowGraphComputed = false;

    public ControlFlowGraph? ControlFlowGraph
    {
        get
        {
            if (!_isControlFlowGraphComputed && Operation != null)
            {
                ComputeControlFlowGraph();
                _isControlFlowGraphComputed = true;
            }
            return _controlFlowGraph;
        }
    }


    public MethodAnalysisContext(IMethodSymbol methodSymbol, IOperation operation)
    {
        MethodSymbol = methodSymbol ?? throw new ArgumentNullException(nameof(methodSymbol));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }


    private void ComputeControlFlowGraph()
    {
        if (Operation is IMethodBodyOperation methodBodyOperation)
        {

            _controlFlowGraph = ControlFlowGraph.Create(methodBodyOperation);
        }
        else if (Operation is IConstructorBodyOperation constructorBodyOperation)
        {

            _controlFlowGraph = ControlFlowGraph.Create(constructorBodyOperation);
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is MethodAnalysisContext context &&
               EqualityComparer<IMethodSymbol>.Default.Equals(MethodSymbol, context.MethodSymbol);
    }
}
