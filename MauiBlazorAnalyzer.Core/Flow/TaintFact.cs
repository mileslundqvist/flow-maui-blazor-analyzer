using MauiBlazorAnalyzer.Core.Flow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace MauiBlazorAnalyzer.Core.Flow;
public sealed class TaintFact : IFact
{
    public AccessPath? Path { get; }
    public IMethodSymbol? ReturnMethod { get; }
    public bool IsReturnValue => Path is null;

    public TaintFact(AccessPath path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public TaintFact(IMethodSymbol m)
    {
        ReturnMethod = m ?? throw new ArgumentNullException(nameof(m));
    }

    public bool AppliesTo(IOperation operation)
    {
        if (IsReturnValue || Path == null) return false;
        return operation switch
        {
            ILocalReferenceOperation l => SymbolEqualityComparer.Default.Equals(l.Local, Path.Base),
            IParameterReferenceOperation p => SymbolEqualityComparer.Default.Equals(p.Parameter, Path.Base),
            IFieldReferenceOperation f => SymbolEqualityComparer.Default.Equals(f.Field, Path.Base),
            _ => false
        };
    }
    public TaintFact WithNewBase(ISymbol newBase)
    {
        if (IsReturnValue) throw new InvalidOperationException("Cannot rebase a return‑value fact");
        return new TaintFact(new AccessPath(newBase, Path!.Fields));
    }
    public override string ToString() => IsReturnValue ? $"Ret({ReturnMethod!.Name})" : Path!.ToString();

    public override bool Equals(object? obj)
        => obj is TaintFact other &&
           (IsReturnValue && other.IsReturnValue && SymbolEqualityComparer.Default.Equals(ReturnMethod, other.ReturnMethod) ||
            !IsReturnValue && !other.IsReturnValue && EqualityComparer<AccessPath>.Default.Equals(Path!, other.Path!));

    public override int GetHashCode()
    {
        return IsReturnValue ? SymbolEqualityComparer.Default.GetHashCode(ReturnMethod) : Path!.GetHashCode();
    }
}
