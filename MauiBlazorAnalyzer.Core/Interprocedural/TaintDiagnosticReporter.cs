using MauiBlazorAnalyzer.Core.Analysis;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using System.Text;

namespace MauiBlazorAnalyzer.Core.Interprocedural;


public sealed record TaintFinding(
    IMethodSymbol Sink,
    int ArgIndex,
    ICFGNode SinkNode,
    TaintFact Fact,
    IReadOnlyList<ExplodedGraphNode> Trace);

public sealed class TaintDiagnosticReporter
{
    private readonly IFDSAnalysisResult _result;
    private readonly InterproceduralCFG _cfg;
    private readonly TaintSpecDB _db = TaintSpecDB.Instance;

    public TaintDiagnosticReporter(IFDSAnalysisResult result, InterproceduralCFG cfg)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    public IEnumerable<TaintFinding> GetFindings()
    {
        foreach ((ICFGNode node, ISet<TaintFact> facts) in _result.Results)
        {
            // Nothing to do if the node has no underlying IOperation
            if (node.Operation is null) continue;

            IEnumerable<IInvocationOperation> invocations = node.Operation
                .DescendantsAndSelf()
                .OfType<IInvocationOperation>();

            foreach (IInvocationOperation inv in invocations)
            {
                if (!_db.IsSink(inv.TargetMethod))
                    continue;

                // We need to check if any of the parameters are tainted.
                for (int i = 0; i < inv.Arguments.Length; ++i)
                {
                    var arg = inv.Arguments[i];
                    HashSet<ISymbol> contributingSymbols = FindContributingBaseSymbols(arg.Value);

                    if (!contributingSymbols.Any()) continue; // No symbols found for this argument

                    foreach (TaintFact fact in facts)
                    {
                        if (fact?.Path?.Base == null) break;

                        foreach (var contributingSymbol in contributingSymbols)
                        {
                            // TODO: Fix better handling of comparison, for some reason SymbolEqualityComparer is returning
                            // false for seamingly the same symbols. A bit hacky solution currently.
                            var contributingString = contributingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            var factString = fact.Path.Base.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            if (contributingString.Equals(factString))
                            {
                                // Match found!
                                var trace = BuildTrace(new ExplodedGraphNode(node, fact));

                                yield return new TaintFinding(
                                    Sink: inv.TargetMethod,
                                    ArgIndex: i,
                                    SinkNode: node,
                                    Fact: fact,
                                    Trace: trace.ToImmutableList());
                                break;
                            }
                        }
                    }
                }

            }
        }
    }


    /// <summary>
    /// Converts the findings to <see cref="AnalysisDiagnostic"/> so they can be surfaced
    /// in editors that understand Roslyn diagnostics.
    /// </summary>
    public IEnumerable<AnalysisDiagnostic> ToDiagnostics(string ruleId = "TAINT0001")
    {
        return GetFindings().Select(finding =>
        {
            Location? diagnosticLocation = finding.SinkNode.Operation?.Syntax.GetLocation() ??
                                          finding.SinkNode.MethodContext.MethodSymbol.Locations.FirstOrDefault();

            FileLinePositionSpan span = diagnosticLocation?.GetLineSpan() ?? new FileLinePositionSpan("Unknown", default, default);

            var messageBuilder = new StringBuilder();
            var sinkMethodDisplay = finding.Sink.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var sinkParameterName = finding.Sink.Parameters.Length > finding.ArgIndex ?
                                    finding.Sink.Parameters[finding.ArgIndex].Name :
                                    $"#{finding.ArgIndex}";

            messageBuilder.AppendLine($"Taint vulnerability detected!");
            messageBuilder.AppendLine($"  Description: Tainted data flows into argument '{sinkParameterName}' (index {finding.ArgIndex}) of sink method '{sinkMethodDisplay}'.");
            messageBuilder.AppendLine($"  Sink Location: {PrettyLocation(finding.SinkNode, true)}"); // Verbose for sink

            string sinkOperationSyntax = finding.SinkNode.Operation?.Syntax.ToString().Trim() ?? GetNodeKindDescription(finding.SinkNode);
            sinkOperationSyntax = System.Text.RegularExpressions.Regex.Replace(sinkOperationSyntax, @"\s+", " ");
            if (sinkOperationSyntax.Length > 120) sinkOperationSyntax = sinkOperationSyntax.Substring(0, 117) + "...";
            messageBuilder.AppendLine($"  Sink Operation: {sinkOperationSyntax}");

            ExplodedGraphNode? sourceExplodedNode = finding.Trace.FirstOrDefault();
            IFact? initialTaintSourceFact = sourceExplodedNode?.Fact;

            if (initialTaintSourceFact != null)
            {
                messageBuilder.AppendLine($"  Original Taint: {initialTaintSourceFact.ToString()}"); // Relies on IFact.ToString()
                if (sourceExplodedNode != null)
                {
                    messageBuilder.AppendLine($"     at Location: {PrettyLocation(sourceExplodedNode?.Node)}");
                }
            }
            else
            {
                messageBuilder.AppendLine($"  Tainted Value: {finding.Fact.ToString()} (direct info at sink, trace may be empty or start differently)");
            }

            messageBuilder.AppendLine("\n  Taint Propagation Trace:");
            if (!finding.Trace.Any())
            {
                messageBuilder.AppendLine("    No detailed trace available (or source is the sink itself).");
            }
            else
            {
                for (int i = 0; i < finding.Trace.Count; i++)
                {
                    ExplodedGraphNode step = finding.Trace[i];
                    string stepLocation = PrettyLocation(step.Node);
                    string operationSyntax = step.Node.Operation?.Syntax.ToString().Trim() ?? GetNodeKindDescription(step.Node);
                    operationSyntax = System.Text.RegularExpressions.Regex.Replace(operationSyntax, @"\s+", " ");
                    if (operationSyntax.Length > 100) operationSyntax = operationSyntax.Substring(0, 97) + "...";

                    string factInfo = $"({step.Fact.ToString()})"; // Use IFact.ToString() which TaintFact/ZeroFact implement

                    string stepPrefix;
                    if (i == 0) stepPrefix = "    [SOURCE] ";
                    // Check if this step's node is the same as the overall finding's SinkNode
                    else if (i == finding.Trace.Count - 1 && step.Node.Equals(finding.SinkNode)) stepPrefix = "    [SINK]   ";
                    else stepPrefix = "    [THROUGH]";

                    messageBuilder.AppendLine($"{stepPrefix}[{stepLocation}] -> {operationSyntax} {factInfo}");
                }
            }

            return new AnalysisDiagnostic(
                id: ruleId,
                title: "Tainted Value Reaches Sink",
                message: messageBuilder.ToString(),
                severity: DiagnosticSeverity.Warning,
                location: span);
        });
    }

    private static string GetNodeKindDescription(ICFGNode node)
    {
        return node.Kind switch
        {
            ICFGNodeKind.Entry => $"Entry to method {node.MethodContext.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            ICFGNodeKind.Exit => $"Exit from method {node.MethodContext.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            ICFGNodeKind.CallSite => $"Call-site in {node.MethodContext.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            ICFGNodeKind.ReturnSite => $"Return-site in {node.MethodContext.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            ICFGNodeKind.Normal => "Intermediate operation",
            _ => "Unknown node kind",
        };
    }

    private ImmutableList<ExplodedGraphNode> BuildTrace(ExplodedGraphNode start)
    {
        var trace = ImmutableList.CreateBuilder<ExplodedGraphNode>();
        var visited = new HashSet<ExplodedGraphNode>();
        var current = start;

        while (visited.Add(current))
        {
            trace.Add(current);
            if (current.Fact is ZeroFact) break;

            if (_result.PathEdges.TryGetValue(current, out var predecessors) && predecessors.Any())
            {
                // Prioritize based on operation syntax location if available and different
                var next = predecessors
                    .OrderBy(p => p.Node.Operation?.Syntax?.GetLocation().SourceSpan.Start)
                    .ThenBy(p => p.Node.GetHashCode()) // Fallback for determinism
                    .FirstOrDefault();
                current = next;
            }
        }

        trace.Reverse();
        return trace.ToImmutable();
    }

    private static string PrettyLocation(ICFGNode n, bool verbose = false)
    {
        string? filePath = null;
        FileLinePositionSpan lineSpan = default;
        bool hasSpecificSyntaxLocation = false;

        Location? nodeLocation = n.Operation?.Syntax.GetLocation();
        if (nodeLocation == null || !nodeLocation.IsInSource)
        {
            nodeLocation = n.MethodContext.MethodSymbol.Locations.FirstOrDefault(loc => loc.IsInSource);
        }

        if (nodeLocation != null && nodeLocation.IsInSource)
        {
            lineSpan = nodeLocation.GetLineSpan();
            filePath = lineSpan.Path;
            if (n.Operation?.Syntax != null) // It's specific if it came from an operation
            {
                hasSpecificSyntaxLocation = true;
            }
        }

        var fileName = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : "UnknownFile";
        var lineNum = lineSpan.StartLinePosition.Line + 1; // 1-based line number

        var locationStr = $"{fileName}:L{lineNum}";

        if (verbose || !hasSpecificSyntaxLocation)
        {
            locationStr += $" (In Method: {n.MethodContext.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}, Kind: {n.Kind})";
        }
        else if (verbose && hasSpecificSyntaxLocation) // If already specific but verbose, add Kind
        {
            locationStr += $" (Kind: {n.Kind})";
        }
        return locationStr;
    }

    private void FindContributingBaseSymbols(IOperation? operation, HashSet<ISymbol> collectedSymbols, int depth = 0)
    {
        // Basic recursion depth limit to prevent stack overflow on complex/cyclic structures
        const int maxDepth = 10;
        if (operation == null || depth > maxDepth || collectedSymbols == null)
        {
            return;
        }

        // --- Base Cases: Direct References ---
        switch (operation)
        {
            case ILocalReferenceOperation l:
                collectedSymbols.Add(l.Local);
                return; // Found a base symbol, stop descent here for this path
            case IParameterReferenceOperation p:
                collectedSymbols.Add(p.Parameter);
                return; // Found a base symbol, stop descent here
            case IFieldReferenceOperation f:
                // Option 1: Consider the field itself as a potential base if your TaintFact supports it.
                // collectedSymbols.Add(f.Field);
                // Option 2 (More common for base symbol tracking): Recurse on the instance
                FindContributingBaseSymbols(f.Instance, collectedSymbols, depth + 1);
                return; // Stop descent after handling instance
            case IPropertyReferenceOperation pr:
                // Similar to fields, often interested in the instance the property is called on.
                // Properties might have complex getters; treat cautiously.
                FindContributingBaseSymbols(pr.Instance, collectedSymbols, depth + 1);
                // You might also consider adding the Property symbol itself if relevant to your TaintFact
                // collectedSymbols.Add(pr.Property);
                return; // Stop descent after handling instance
            case IInstanceReferenceOperation i: // 'this' or 'base' reference
                                                // You might represent 'this' with the containing type symbol or a special marker
                                                // collectedSymbols.Add(i.Type); // Example: using the type symbol
                return;
            case ILiteralOperation lit:
                // Literals generally aren't base symbols unless representing specific sensitive strings
                return;
        }

        // --- Recursive Steps for Common Operations ---
        switch (operation)
        {
            case IConversionOperation conv:
                FindContributingBaseSymbols(conv.Operand, collectedSymbols, depth + 1);
                break;
            case IBinaryOperation binOp:
                FindContributingBaseSymbols(binOp.LeftOperand, collectedSymbols, depth + 1);
                FindContributingBaseSymbols(binOp.RightOperand, collectedSymbols, depth + 1);
                break;
            case IUnaryOperation unOp:
                FindContributingBaseSymbols(unOp.Operand, collectedSymbols, depth + 1);
                break;
            case IInvocationOperation inv:
                // Recurse on arguments and the instance receiver
                FindContributingBaseSymbols(inv.Instance, collectedSymbols, depth + 1);
                foreach (var arg in inv.Arguments)
                {
                    FindContributingBaseSymbols(arg.Value, collectedSymbols, depth + 1);
                }
                // Note: We usually DON'T add the invocation's result symbol here,
                // as taint flow through calls is handled by the main IFDS analysis.
                // We only care about the symbols *used* to make the call or provide its arguments.
                break;
            case IInterpolatedStringOperation interp:
                // Recurse on the expression parts of the interpolation
                foreach (var part in interp.Parts.OfType<IInterpolationOperation>())
                {
                    FindContributingBaseSymbols(part.Expression, collectedSymbols, depth + 1);
                    // Optional: Also check part.Alignment and part.FormatClause if they can be tainted
                }
                break;
            case IMemberReferenceOperation memRef: // Catches fields/properties again, but useful as fallback
                FindContributingBaseSymbols(memRef.Instance, collectedSymbols, depth + 1);
                // Optionally add memRef.Member here if TaintFact tracks specific members
                // collectedSymbols.Add(memRef.Member);
                break;
            case IArgumentOperation argOp: // Handle Argument operations directly if needed
                FindContributingBaseSymbols(argOp.Value, collectedSymbols, depth + 1);
                break;
            case IObjectCreationOperation objCreation:
                foreach (var arg in objCreation.Arguments)
                {
                    FindContributingBaseSymbols(arg.Value, collectedSymbols, depth + 1);
                }
                // Handle Initializer if present
                FindContributingBaseSymbols(objCreation.Initializer, collectedSymbols, depth + 1);
                break;
            case IObjectOrCollectionInitializerOperation initializer:
                foreach (var memberInit in initializer.Initializers)
                {
                    FindContributingBaseSymbols(memberInit, collectedSymbols, depth + 1);
                }
                break;
            case ISimpleAssignmentOperation assignmentTarget: // When initializer target is assigned
                                                              // This case might need specific handling if analyzing initializers directly
                                                              // For argument checking, usually handled by top-level switch (ILocalRef, etc.)
                break;
            // --- Default: Recurse on all children ---
            // Fallback for operations not explicitly handled above
            default:
                foreach (var child in operation.ChildOperations)
                {
                    FindContributingBaseSymbols(child, collectedSymbols, depth + 1);
                }
                break;
        }
    }

    // Overload to start the process
    private HashSet<ISymbol> FindContributingBaseSymbols(IOperation operation)
    {
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.IncludeNullability);
        FindContributingBaseSymbols(operation, symbols);
        return symbols;
    }

}