using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Diagnostics;


namespace MauiBlazorAnalyzer.Core.Intraprocedural.CallGraph;
public class CallGraphBuilder : OperationWalker
{
    private readonly Compilation _compilation;
    private readonly Project _project;

    private CallGraph _callGraph = null!;
    private Stack<IMethodSymbol> _currentMethodStack = null!; 

    public CallGraphBuilder(Project project, Compilation compilation)
    {
        _compilation = compilation;
        _project = project;
    }

    public CallGraph Build(IEnumerable<IOperation> methodEntryPoints, CancellationToken cancellationToken = default)
    {
        _callGraph = new();
        _currentMethodStack = new Stack<IMethodSymbol>();

        foreach (var entryPoint in methodEntryPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entryPoint is null) continue;

            IMethodSymbol? entryPointSymbol = GetMethodSymbolForEntryPoint(entryPoint);

            if (entryPointSymbol != null)
            {
                _currentMethodStack.Push(entryPointSymbol);

                this.Visit(entryPoint);

                if (_currentMethodStack.Count > 0 && SymbolEqualityComparer.Default.Equals(_currentMethodStack.Peek(), entryPointSymbol))
                {
                    _currentMethodStack.Pop();
                }
                else
                {
                    Debug.Fail("Method context stack corruption detected");
                }
            }
        }

        return _callGraph;
    }

    private IMethodSymbol? GetMethodSymbolForEntryPoint(IOperation operation)
    {
        if (operation is null) return null;
        if (operation.SemanticModel is null) return null;

        ISymbol? declaredSymbol = operation.SemanticModel.GetDeclaredSymbol(operation.Syntax);

        if (declaredSymbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol;
        }

        if (operation is IAnonymousFunctionOperation anonymousFunctionOperation)
        {
            return anonymousFunctionOperation.Symbol;
        }

        if (operation is ILocalFunctionOperation localFunctionOperation)
        {
            return localFunctionOperation.Symbol;
        }

        return null;
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
            _callGraph.AddEdge(caller, callee);
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
            _callGraph.AddEdge(caller, callee);
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
