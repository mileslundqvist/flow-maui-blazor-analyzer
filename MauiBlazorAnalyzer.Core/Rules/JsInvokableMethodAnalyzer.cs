using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Rules;

/// <summary>
/// Analyzer to detect methods marked with the [JSInvokable] attribute.
/// </summary>
public class JsInvokableMethodAnalyzer : IAnalyzer
{
    public string Id => "MBA001";
    public string Name => "JSInvokable Method Detection";
    public string Description => "Detects C# methods exposed to JavaScript via [JSInvokable].";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;

    // Define a Roslyn DiagnosticDescriptor for this specific rule
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "MBA001",
        title: "JSInvokable Method Found",
        messageFormat: "Method '{0}' is exposed to JavaScript via [JSInvokable]. Ensure this is intended and secure.",
        category: "MauiBlazorHybrid",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Methods marked with [JSInvokable] can be called directly from JavaScript code, representing a key part of the application's attack surface.");

    public Task<ImmutableArray<AnalysisDiagnostic>> AnalyzeCompilationAsync(
        Project project,
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            // Use a walker specifically designed to find [JSInvokable] attributes
            var walker = new JsInvokableAttributeWalker(semanticModel, diagnostics, Rule, cancellationToken);
            walker.Visit(syntaxTree.GetRoot(cancellationToken));
        }
        return Task.FromResult(diagnostics.ToImmutable());
    }

    private class JsInvokableAttributeWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly ImmutableArray<AnalysisDiagnostic>.Builder _diagnostics;
        private readonly DiagnosticDescriptor _descriptor;
        private readonly CancellationToken _cancellationToken;

        private const string JsInvokableAttributeFullName = "Microsoft.JSInterop.JSInvokableAttribute";

        public JsInvokableAttributeWalker(
            SemanticModel semanticModel,
            ImmutableArray<AnalysisDiagnostic>.Builder diagnosticsBuilder,
            DiagnosticDescriptor descriptor,
            CancellationToken cancellationToken)
            : base(SyntaxWalkerDepth.Node)
        {
            _semanticModel = semanticModel;
            _diagnostics = diagnosticsBuilder;
            _descriptor = descriptor;
            _cancellationToken = cancellationToken;
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // Get the type symbol for the attribute being applied
            var typeInfo = _semanticModel.GetTypeInfo(node, _cancellationToken);
            var displayString = typeInfo.Type?.ToDisplayString();

            // Check if the attribute type's full name matches JSInvokableAttribute
            if (displayString == JsInvokableAttributeFullName)
            {
                // Find the method this attribute is attached to
                MethodDeclarationSyntax? methodDeclaration = node.Parent?.Parent as MethodDeclarationSyntax;

                if (methodDeclaration != null)
                {
                    // Get the method symbol
                    var methodSymbol = _semanticModel.GetDeclaredSymbol(methodDeclaration, _cancellationToken);
                    string methodName = methodSymbol?.Name ?? methodDeclaration.Identifier.ValueText;

                    // Report the diagnostic at the location of the attribute
                    var location = node.GetLocation();
                    var diagnostic = Diagnostic.Create(_descriptor, location, methodName);

                    // Convert to your custom diagnostic format
                    _diagnostics.Add(AnalysisDiagnostic.FromRoslynDiagnostic(diagnostic));
                }
            }
        }
    }
}

