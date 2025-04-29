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
                    var baseSymbol = GetBaseSymbol(arg.Value);
                    if (baseSymbol is null) continue;

                    // Any tainted fact whose access-path *base* matches?
                    if (facts.FirstOrDefault(f => SymbolEqualityComparer.Default.Equals(
                                                    f.Path.Base, baseSymbol))
                        is not { } matchedFact)
                        continue;

                    var trace = BuildOneTrace(new ExplodedGraphNode(node, matchedFact));

                    yield return new TaintFinding(
                        Sink: inv.TargetMethod,
                        ArgIndex: i,
                        SinkNode: node,
                        Fact: matchedFact,
                        Trace: trace);
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
}