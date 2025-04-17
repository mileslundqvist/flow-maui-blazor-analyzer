using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MauiBlazorAnalyzer.Core.Interprocedural;

public enum ICFGNodeKind { Normal, CallSite, Entry, Exit, ReturnSite }

public class ICFGNode
{
    public IOperation Operation { get; }
    public MethodAnalysisContext MethodContext { get; }
    public ICFGNodeKind Kind { get; }
    

    public ICFGNode(IOperation operation, MethodAnalysisContext context, ICFGNodeKind kind = ICFGNodeKind.Normal)
    {
        Operation = operation;
        MethodContext = context;
        Kind = kind;
    }

    public override bool Equals(object? obj)
    {
        if (obj is ICFGNode other)
        {
            bool operationEquals = (Operation == null && other.Operation == null) ||
                (Operation?.Syntax.GetLocation().SourceSpan == other.Operation?.Syntax.GetLocation().SourceSpan);

            return Kind == other.Kind && SymbolEqualityComparer.Default.Equals(MethodContext.MethodSymbol, other.MethodContext.MethodSymbol) &&
                operationEquals;
        }
        return false;
    }

    public override int GetHashCode()
    {
        var opHash = Operation?.Syntax.GetLocation().SourceSpan.GetHashCode() ?? 0;
        var methodHash = SymbolEqualityComparer.Default.GetHashCode(MethodContext.MethodSymbol);
        return HashCode.Combine(Kind, methodHash, opHash);
    }
}
