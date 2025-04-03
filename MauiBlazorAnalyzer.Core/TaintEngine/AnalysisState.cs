using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class AnalysisState : IEquatable<AnalysisState>
{
    public ImmutableDictionary<ISymbol, TaintState> TaintMap { get; }

    public AnalysisState(ImmutableDictionary<ISymbol, TaintState> map)
    {
        TaintMap = map ?? throw new ArgumentNullException(nameof(map));
    }
    public static AnalysisState Empty { get; } = new(ImmutableDictionary<ISymbol, TaintState>.Empty);

    public TaintState GetTaint(ISymbol symbol) =>
        TaintMap.TryGetValue(symbol, out var state) ? state : TaintState.NotTainted;

    public AnalysisState SetTaint(ISymbol symbol, TaintState state)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (GetTaint(symbol) == state) return this;
        return new AnalysisState(TaintMap.SetItem(symbol, state));
    }

    public AnalysisState Merge(AnalysisState other)
    {
        var builder = TaintMap.ToBuilder();
        bool changed = false;
        foreach (var kvp in other.TaintMap)
        {
            if (kvp.Value == TaintState.Tainted && GetTaint(kvp.Key) == TaintState.NotTainted)
            {
                builder[kvp.Key] = TaintState.Tainted;
                changed = true;
            }
        }
        return changed ? new AnalysisState(builder.ToImmutable()) : this;
    }

    public bool Equals(AnalysisState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (TaintMap.Count != other.TaintMap.Count) return false;
        foreach (var kvp in TaintMap)
        {
            if (!other.TaintMap.TryGetValue(kvp.Key, out var otherValue) || kvp.Value != otherValue)
            {
                return false;
            }
        }
        return true;
    }
    public override bool Equals(object? obj) => Equals(obj as AnalysisState);

    public override int GetHashCode()
    {
        int hash = 19;
        foreach (var kvp in TaintMap.OrderBy(kv => kv.Key.Name)) // Order for consistency
        {
            hash = hash * 31 + kvp.Key.GetHashCode();
            hash = hash * 31 + kvp.Value.GetHashCode();
        }
        return hash;
    }

    public static bool operator ==(AnalysisState? left, AnalysisState? right) => Equals(left, right);
    public static bool operator !=(AnalysisState? left, AnalysisState? right) => !Equals(left, right);
}
