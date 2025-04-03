using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Rules;

/// <summary>
/// Analyzer to detect intra-procedural flow from [JSInvokable] parameters to known sensitive sinks.
/// </summary>
public class JsInvokableIntraproceduralSinkAnalyzer : IAnalyzer
{
    public string Id => "MBA002";
    public string Name => "Potential Taint Flow from JSInvokable Parameter to Sink";
    public string Description => "Detects if data from a [JSInvokable] method parameter potentially flows to a sensitive sink within the same method.";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;

    // --- Define Sink Methods (Customize this list) ---
    private static readonly IImmutableSet<string> SinkMethodSignatures = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        // File System
        "System.IO.File.WriteAllText",
        "System.IO.File.WriteAllBytes",
        "System.IO.File.AppendAllText",
        "System.IO.StreamWriter.Write",
        "System.IO.StreamWriter.WriteLine",
        // Networking
        "System.Net.Http.HttpClient.GetStringAsync", // Sink if URL is tainted
        "System.Net.Http.HttpClient.PostAsync",      // Sink if content or URL is tainted
        "System.Net.Http.HttpClient.PutAsync",       // Sink if content or URL is tainted
                                                     // Logging (Use with caution - depends on what's logged)
        "Microsoft.Extensions.Logging.ILogger.LogInformation",
        "Microsoft.Extensions.Logging.ILogger.LogWarning",
        "Microsoft.Extensions.Logging.ILogger.LogError",
        "System.Console.WriteLine",
        "System.Diagnostics.Debug.WriteLine",
        // MAUI Essentials/Platform Specific (Examples)
        "Microsoft.Maui.ApplicationModel.Communication.Sms.ComposeAsync",
        "Microsoft.Maui.Storage.FileSystem.OpenAppPackageFileAsync", // If path comes from param
        "Microsoft.Maui.Storage.SecureStorage.SetAsync" // If value comes from param
                                                        // Add others: Database execution, Process.Start, etc.
    );

    private static bool IsSinkMethod(IMethodSymbol? methodSymbol)
    {
        if (methodSymbol == null) return false;

        string fullName = methodSymbol.OriginalDefinition.ToString();
        string simpleName = $"{methodSymbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{methodSymbol.Name}";

        return SinkMethodSignatures.Contains(fullName.Split('<', '(')[0]) ||
               SinkMethodSignatures.Contains(simpleName);
    }

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: "MBA002",
        title: "Potential Taint Flow from JSInvokable Parameter",
        messageFormat: "Data from JSInvokable parameter '{0}' potentially flows to sensitive sink '{1}' here.",
        category: "MauiBlazorHybridSecurity",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Data originating from JavaScript via [JSInvokable] parameters should be validated/sanitized before being used in sensitive operations (sinks).");

    // Main analysis method called by the orchestrator
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
            var root = syntaxTree.GetRoot(cancellationToken); // Get root synchronously within loop

            // Use a dedicated walker for finding and analyzing JSInvokable methods
            var walker = new JsInvokableTaintWalker(semanticModel, diagnostics, Rule, cancellationToken);
            walker.Visit(root);
       
        }
        return Task.FromResult(diagnostics.ToImmutable());
    }

    // --- Syntax Walker ---
    private class JsInvokableTaintWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly ImmutableArray<AnalysisDiagnostic>.Builder _diagnostics;
        private readonly DiagnosticDescriptor _descriptor;
        private readonly CancellationToken _cancellationToken;

        private const string JsInvokableAttributeFullName = "Microsoft.JSInterop.JSInvokableAttribute";

        public JsInvokableTaintWalker(
            SemanticModel semanticModel,
            ImmutableArray<AnalysisDiagnostic>.Builder diagnosticsBuilder,
            DiagnosticDescriptor descriptor,
            CancellationToken cancellationToken)
            : base(SyntaxWalkerDepth.Node) // Can optimize depth if only looking at methods
        {
            _semanticModel = semanticModel;
            _diagnostics = diagnosticsBuilder;
            _descriptor = descriptor;
            _cancellationToken = cancellationToken;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax methodDeclaration)
        {
            _cancellationToken.ThrowIfCancellationRequested();



            var methodSymbol = _semanticModel.GetDeclaredSymbol(methodDeclaration, _cancellationToken);

            // Proceed only if it's a JSInvokable method with parameters and a body
            if (IsJsInvokableMethodWithParameters(methodSymbol) &&
                TryGetMethodBody(methodDeclaration, out var methodBodyNode))
            {
                AnalyzeMethodBodyForTaintFlow(methodSymbol!, sourceParameters: methodSymbol!.Parameters, methodBodyNode);
            }

            // Continue walking potential nested methods/lambdas
            base.VisitMethodDeclaration(methodDeclaration);
        }

        // Helper to check if the method is relevant for this analysis
        private bool IsJsInvokableMethodWithParameters(IMethodSymbol? methodSymbol)
        {
            return methodSymbol != null &&
                   methodSymbol.Parameters.Any() && // Must have parameters to be a source
                   methodSymbol.GetAttributes().Any(attr =>
                        attr.AttributeClass?.ToDisplayString() == JsInvokableAttributeFullName);
        }

        // Helper to get the method body node
        private bool TryGetMethodBody(MethodDeclarationSyntax methodDeclaration, out SyntaxNode? methodBodyNode)
        {
            methodBodyNode = methodDeclaration.Body ?? (SyntaxNode?)methodDeclaration.ExpressionBody?.Expression;
            return methodBodyNode != null;
        }

        // Main analysis logic for a single method body
        private void AnalyzeMethodBodyForTaintFlow(
            IMethodSymbol methodSymbol,
            ImmutableArray<IParameterSymbol> sourceParameters,
            SyntaxNode methodBodyNode)
        {
            // Perform data flow analysis once for the entire method body
            DataFlowAnalysis? methodDataFlow = _semanticModel.AnalyzeDataFlow(methodBodyNode);
            if (methodDataFlow == null || !methodDataFlow.Succeeded)
            {
                // Log or handle error: Could not analyze data flow for methodSymbol.Name
                return;
            }

            // Find all sink invocations within this method body
            var sinkInvocations = methodBodyNode.DescendantNodes()
                                                .OfType<InvocationExpressionSyntax>()
                                                .Select(inv => (InvocationNode: inv, InvokedSymbol: _semanticModel.GetSymbolInfo(inv, _cancellationToken).Symbol as IMethodSymbol))
                                                .Where(invInfo => IsSinkMethod(invInfo.InvokedSymbol))
                                                .ToList();

            if (!sinkInvocations.Any()) return; // No sinks found in this method

            // Get all symbols read within the method body (potential carriers of taint)
            var symbolsReadInMethod = methodDataFlow.ReadInside.Union(methodDataFlow.DataFlowsIn, SymbolEqualityComparer.Default).ToImmutableHashSet(SymbolEqualityComparer.Default);

            // Check if any source parameter is read within the method body at all
            // If not, no flow is possible (simplification)
            bool anySourceParameterRead = sourceParameters.Any(p => symbolsReadInMethod.Contains(p));
            if (!anySourceParameterRead) return;


            foreach (var (invocationNode, sinkMethodSymbol) in sinkInvocations)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // Check each argument passed to the sink
                for (int i = 0; i < invocationNode.ArgumentList.Arguments.Count; i++)
                {
                    var argument = invocationNode.ArgumentList.Arguments[i];
                    _cancellationToken.ThrowIfCancellationRequested();

                    // Check flow from *any* source parameter to *this* argument
                    if (CheckPotentialFlow(sourceParameters, argument.Expression, methodDataFlow, symbolsReadInMethod))
                    {
                        // Report diagnostic (consider reporting only once per sink call or per parameter)
                        var diagnostic = Diagnostic.Create(
                            _descriptor,
                            argument.GetLocation(), // Location of the tainted argument
                            string.Join(", ", sourceParameters.Select(p => p.Name)), // Indicate which params *could* flow
                            sinkMethodSymbol?.Name ?? "Unknown Sink", // Sink method name
                            argument.ToString() // Show the argument expression
                        );
                        _diagnostics.Add(AnalysisDiagnostic.FromRoslynDiagnostic(diagnostic));
                        // Optional: `break;` here if you only want one finding per sink invocation
                    }
                }
            }
        }

        /// <summary>
        /// Performs a basic check to see if data originating from source parameters
        /// *could* potentially flow into the target expression within the method's scope.
        /// NOTE: This is an INTRA-PROCEDURAL and APPROXIMATE check. It does NOT perform
        /// precise taint tracking through complex assignments or control flow. It checks
        /// for direct usage or usage after potential modification within the method body
        /// if the source parameter is read anywhere within that body.
        /// </summary>
        private bool CheckPotentialFlow(
            ImmutableArray<IParameterSymbol> sourceParameters,
            ExpressionSyntax targetExpression,
            DataFlowAnalysis methodDataFlow,
            IImmutableSet<ISymbol> symbolsReadInMethod
            )
        {
            // Analyze data flow specifically for the target argument expression
            DataFlowAnalysis? targetDataFlow = _semanticModel.AnalyzeDataFlow(targetExpression);
            if (targetDataFlow?.Succeeded != true) return false; // Cannot analyze argument flow

            // Get all symbols directly read by the target expression or flowing into it
            var symbolsReadByTarget = targetDataFlow.ReadInside.Union(targetDataFlow.DataFlowsIn, SymbolEqualityComparer.Default);

            foreach (var symbolRead in symbolsReadByTarget)
            {
                ISymbol? underlyingSymbol = GetUnderlyingSymbol(symbolRead);
                if (underlyingSymbol == null) continue;

                // Direct Flow Check: Is the symbol read by the argument one of the source parameters?
                if (sourceParameters.Contains(underlyingSymbol, SymbolEqualityComparer.Default))
                {
                    return true; // Direct flow detected
                }

                // Indirect Flow Check (Approximate):
                // Is the symbol read by the argument a local variable or parameter (other than source)
                // AND was that symbol written to within the method body
                // AND was *any* source parameter read anywhere in the method body?
                if ((underlyingSymbol is ILocalSymbol || (underlyingSymbol is IParameterSymbol && !sourceParameters.Contains(underlyingSymbol, SymbolEqualityComparer.Default))) &&
                    methodDataFlow.WrittenInside.Contains(underlyingSymbol, SymbolEqualityComparer.Default) /* && anySourceParameterRead (already checked outside) */ )
                {
                    // We assume potential flow: a source parameter was read somewhere in the method,
                    // and the variable used in the sink was written to somewhere in the method.
                    // This is an OVER-APPROXIMATION. A precise check would require
                    // tracking specific assignments.
                    return true;
                }
            }

            return false; // No potential flow detected by this basic check
        }

        // Helper to get the primary symbol (parameter, local, field, etc.)
        private ISymbol? GetUnderlyingSymbol(ISymbol? symbol) => symbol switch
        {
            ILocalSymbol l => l,
            IParameterSymbol p => p,
            IFieldSymbol f => f,
            IPropertySymbol prop => prop,
            IMethodSymbol m => m, // Less likely for data flow argument, but possible
            _ => symbol // Return original if not one of the common types
        };
    }
}
