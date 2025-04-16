using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace MauiBlazorAnalyzer.Core.Intraprocedural.Context;
public static class MethodAnalysisContextProvider
{
    public static async Task<Dictionary<IMethodSymbol, MethodAnalysisContext>> GetMethodAnalysisContexts(Compilation compilation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation, nameof(compilation));

        var syntaxFinder = new EntryPointSyntaxFinder();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            if (root != null)
            {
                syntaxFinder.Visit(root);
            }
        }

        var methodAnalysisContexts = new Dictionary<IMethodSymbol, MethodAnalysisContext>(SymbolEqualityComparer.Default);

        foreach (var node in syntaxFinder.EntryPointNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(node.SyntaxTree);
            IOperation? operation = model.GetOperation(node, cancellationToken);
            ISymbol? symbol = model.GetDeclaredSymbol(node, cancellationToken);

            if (operation != null && symbol != null)
            {
                if (symbol is IMethodSymbol methodSymbol)
                {
                    MethodAnalysisContext methodAnalysisContext = new(methodSymbol, operation);
                    methodAnalysisContexts.Add(methodSymbol, methodAnalysisContext);
                }
            }
        }

        return methodAnalysisContexts;
    }




    private class EntryPointSyntaxFinder : CSharpSyntaxWalker
    {
        private readonly List<SyntaxNode> _entryPointNodes = new();
        public IReadOnlyList<SyntaxNode> EntryPointNodes => _entryPointNodes;

        public EntryPointSyntaxFinder() : base(SyntaxWalkerDepth.Node) { }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (node.Body != null || node.ExpressionBody != null)
            {
                _entryPointNodes.Add(node);
            }

            base.VisitLocalFunctionStatement(node);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _entryPointNodes.Add(node);
            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _entryPointNodes.Add(node);
            base.VisitSimpleLambdaExpression(node);
        }

    }
}
