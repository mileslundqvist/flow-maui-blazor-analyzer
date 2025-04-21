using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Intraprocedural.Context;
public class MethodAnalysisContext
{
    public readonly IMethodSymbol MethodSymbol;
    public IOperation RootOperation;

    public MethodAnalysisContext(IMethodSymbol methodSymbol, IOperation operation = null)
    {
        MethodSymbol = methodSymbol ?? throw new ArgumentNullException(nameof(methodSymbol));
        RootOperation = operation;
    }

    public IOperation? EnsureOperation(Compilation compilation)
    {
        if (RootOperation != null) return RootOperation;

        var decl = MethodSymbol.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<BaseMethodDeclarationSyntax>()
            .FirstOrDefault(d => d.Body != null || d.ExpressionBody != null);

        if (decl == null) return null;
        var model = compilation.GetSemanticModel(decl.SyntaxTree);
        RootOperation = model.GetOperation(decl);
        return RootOperation;
    }

    public override bool Equals(object? obj)
    {
        return obj is MethodAnalysisContext context &&
               EqualityComparer<IMethodSymbol>.Default.Equals(MethodSymbol, context.MethodSymbol);
    }
}
