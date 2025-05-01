using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis;
using MauiBlazorAnalyzer.Core.Analysis;

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
                        if (fact?.Path?.Base != null && contributingSymbols.Contains(fact.Path.Base))
                        {
                            // Match found!
                            var trace = BuildOneTrace(new ExplodedGraphNode(node, fact));

                            yield return new TaintFinding(
                                Sink: inv.TargetMethod,
                                ArgIndex: i,
                                SinkNode: node,
                                Fact: fact,
                                Trace: trace);
                            break;
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
        int seq = 0;
        foreach (TaintFinding f in GetFindings())
        {
            var loc = f.SinkNode.Operation?.Syntax.GetLocation();
            FileLinePositionSpan span = (loc is null)
                ? new FileLinePositionSpan("Unknown", default, default)
                : loc.GetLineSpan();

            string msg =
                $"Tainted data flows into sink '{f.Sink.ToDisplayString()}' " +
                $"(argument #{f.ArgIndex}).";

            yield return new AnalysisDiagnostic(
                id: ruleId,
                title: "Tainted value passed to sink",
                message: msg,
                severity: DiagnosticSeverity.Warning,
                location: span,
                helpLink: "https://OWASP.org/www-community/vulnerabilities"
            );
            seq++;
        }
    }


    public void WriteConsoleReport(TextWriter writer)
    {
        foreach (TaintFinding f in GetFindings())
        {
            writer.WriteLine("────────────────────────────────────────────────────");
            writer.WriteLine($"Sink      : {f.Sink.ToDisplayString()}  (arg #{f.ArgIndex})");
            writer.WriteLine($"Location  : {PrettyLocation(f.SinkNode)}");
            writer.WriteLine($"Taint fact: {f.Fact}");
            writer.WriteLine("Trace:");
            foreach (var step in f.Trace)
            {
                writer.WriteLine($"   ↳ {PrettyLocation(step.Node)} | {step.Fact}");
            }
            writer.WriteLine();
        }
    }

    private static ISymbol? GetBaseSymbol(IOperation op) => op switch
    {
        ILocalReferenceOperation l => l.Local,
        IParameterReferenceOperation p => p.Parameter,
        IFieldReferenceOperation f => f.Member,
        IPropertyReferenceOperation pr => pr.Property,
        //IInterpolatedStringOperation iso => iso.
        _ => null
    };

    private static string PrettyLocation(ICFGNode n)
    {
        if (n.Operation?.Syntax is { } syn)
        {
            var span = syn.SyntaxTree.GetLineSpan(syn.Span);
            return $"{Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
        }
        return "<unknown>";
    }

    private IReadOnlyList<ExplodedGraphNode> BuildOneTrace(ExplodedGraphNode start)
    {
        var trace = new List<ExplodedGraphNode> { start };
        var seen = new HashSet<ExplodedGraphNode> { start };

        var cur = start;
        while (_result.PathEdges.TryGetValue(cur, out var preds) && preds.Count > 0)
        {
            // Take *one* predecessor (arbitrary but deterministic).
            var next = preds.OrderBy(p => p.Node.GetHashCode()).First();

            if (!seen.Add(next)) break; // safety – cycle
            trace.Add(next);

            if (next.Fact is ZeroFact) break; // reached seed
            cur = next;
        }
        trace.Reverse();
        return trace;
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
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        FindContributingBaseSymbols(operation, symbols);
        return symbols;
    }

}