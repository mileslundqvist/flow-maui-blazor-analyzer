using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public record TaintFact : IFact
{

    public ISymbol? TaintedSymbol { get; }

    public TaintFact(ISymbol? taintedSymbol)
    {
        ArgumentNullException.ThrowIfNull(taintedSymbol);
        TaintedSymbol = taintedSymbol;
    }

    public virtual bool Equals(TaintFact? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SymbolEqualityComparer.Default.Equals(TaintedSymbol, other.TaintedSymbol);
    }

    public override int GetHashCode()
    {
        return SymbolEqualityComparer.Default.GetHashCode(TaintedSymbol);
    }
}
