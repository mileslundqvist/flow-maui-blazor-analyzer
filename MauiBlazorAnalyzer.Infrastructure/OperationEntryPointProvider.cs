using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MauiBlazorAnalyzer.Infrastructure;
public static class OperationEntryPointProvider
{
    public static async Task<IEnumerable<IOperation>> GetEntryPointsAsync(
        Compilation compilation,
        CancellationToken cancellationToken = default)
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

        var entryPoints = new List<IOperation>(syntaxFinder.EntryPointNodes.Count);
        foreach (var node in syntaxFinder.EntryPointNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(node.SyntaxTree);
            IOperation? operation = model.GetOperation(node, cancellationToken);
            if (operation != null)
            {
                entryPoints.Add(operation);
            }
        }

        return entryPoints;
    }

    private class EntryPointSyntaxFinder : CSharpSyntaxWalker
    {
        private readonly List<SyntaxNode> _entryPointNodes = new();
        public IReadOnlyList<SyntaxNode> EntryPointNodes => _entryPointNodes;

        public EntryPointSyntaxFinder() : base (SyntaxWalkerDepth.Node) { }

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
