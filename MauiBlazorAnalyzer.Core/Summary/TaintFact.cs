using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Summary;

public readonly struct TaintFact : IEquatable<TaintFact>
{
    public int Index { get; }
    public string Name { get; }

    public TaintFact(int index, string name)
    {
        Index = index;
        Name = name;
    }

    public override int GetHashCode() => Index;
    public bool Equals(TaintFact other) => Index == other.Index;
    public override bool Equals(object obj) => obj is TaintFact other && Equals(other);
    public override string ToString() => Name;
}

