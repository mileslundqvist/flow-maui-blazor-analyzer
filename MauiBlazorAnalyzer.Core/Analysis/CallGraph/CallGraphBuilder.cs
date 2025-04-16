using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Diagnostics;


namespace MauiBlazorAnalyzer.Core.Analysis.CallGraph;
public class CallGraphBuilder : OperationWalker
{

    private CallGraph _callGraph = null!;
    private Stack<IMethodSymbol> _currentMethodStack = null!;
    private Dictionary<IMethodSymbol, MethodAnalysisContext> _contexts;

    public CallGraph Build(Dictionary<IMethodSymbol, MethodAnalysisContext> contexts, CancellationToken cancellationToken = default)
    {
        _callGraph = new();
        _currentMethodStack = new Stack<IMethodSymbol>();
        _contexts = contexts;

        foreach (var kvp in contexts)
        {
            IMethodSymbol methodSymbol = kvp.Key;
            MethodAnalysisContext context = kvp.Value;

            cancellationToken.ThrowIfCancellationRequested();


            _currentMethodStack.Push(methodSymbol);

            Visit(context.Operation);

            if (_currentMethodStack.Count > 0 && SymbolEqualityComparer.Default.Equals(_currentMethodStack.Peek(), methodSymbol))
            {
                _currentMethodStack.Pop();
            }
            else
            {
                Debug.Fail("Method context stack corruption detected");
            }
            
        }

        return _callGraph;
    }

    public override void VisitLocalFunction(ILocalFunctionOperation operation)
    {
        var symbol = operation.Symbol;
        _currentMethodStack.Push(symbol);
        try
        {
            base.VisitLocalFunction(operation);
        }
        finally
        {
            if (_currentMethodStack.Count > 0 && SymbolEqualityComparer.Default.Equals(_currentMethodStack.Peek(), symbol))
            {
                _currentMethodStack.Pop();
            }
            else
            {
                Debug.Fail("Stack corruption: LocalFunction");
            }
        }
    }

    public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
    {
        var symbol = operation.Symbol;
        _currentMethodStack.Push(symbol);
        try
        {
            base.VisitAnonymousFunction(operation);
        }
        finally
        {
            // Ensure pop even on exceptions
            if (_currentMethodStack.Count > 0 && SymbolEqualityComparer.Default.Equals(_currentMethodStack.Peek(), symbol))
            {
                _currentMethodStack.Pop();
            }
            else { Debug.Fail("Stack corruption: AnonymousFunction"); }
        }
    }

    public override void VisitInvocation(IInvocationOperation operation)
    {
        if (_currentMethodStack.Count > 0)
        {
            var caller = _currentMethodStack.Peek();
            var callee = operation.TargetMethod;

            // Add the edge: caller -> callee
            if (_contexts.TryGetValue(callee, out var value))
            {
                if (_contexts.TryGetValue(caller, out var callerContext))
                {
                    _callGraph.AddEdge(callerContext, value);
                }
                else
                {
                    var newCallerContext = new MethodAnalysisContext(caller, operation);
                    _callGraph.AddEdge(newCallerContext, value);
                }
                
            }
        }
        
        base.VisitInvocation(operation);
    }

    public override void VisitObjectCreation(IObjectCreationOperation operation)
    {
        if (_currentMethodStack.Count > 0)
        {
            var caller = _currentMethodStack.Peek();
            var callee = operation.Constructor;

            // Add the edge: caller -> callee (constructor)
            if (_contexts.TryGetValue(callee, out var value))
            {
                if (_contexts.TryGetValue(caller, out var callerContext))
                {
                    _callGraph.AddEdge(callerContext, value);
                }
                else
                {
                    var newCallerContext = new MethodAnalysisContext(caller, operation);
                    _callGraph.AddEdge(newCallerContext, value);
                }
            }
        }

        base.VisitObjectCreation(operation);
    }

    public override void VisitMethodBodyOperation(IMethodBodyOperation operation)
    {
        base.VisitMethodBodyOperation(operation);
    }

    public override void VisitConstructorBodyOperation(IConstructorBodyOperation operation)
    {
        base.VisitConstructorBodyOperation(operation);
    }
}
