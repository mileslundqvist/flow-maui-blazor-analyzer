using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class SummaryManager
{
    private readonly ConcurrentDictionary<(IMethodSymbol Method, TaintInputPattern Input), TaintSummary> _summaryCache =
           new ConcurrentDictionary<(IMethodSymbol, TaintInputPattern), TaintSummary>(MethodInputComparer.Instance);

    public bool TryGetSummary(IMethodSymbol method, TaintInputPattern inputPattern, out TaintSummary summary)
    {
        return _summaryCache.TryGetValue((method, inputPattern), out summary!);
    }

    public void StoreSummary(IMethodSymbol method, TaintInputPattern inputPattern, TaintSummary summary)
    {
        _summaryCache[(method, inputPattern)] = summary;
    }

    // Custom comparer for the dictionary key tuple
    private class MethodInputComparer : IEqualityComparer<(IMethodSymbol Method, TaintInputPattern Input)>
    {
        public static readonly MethodInputComparer Instance = new MethodInputComparer();

        public bool Equals((IMethodSymbol Method, TaintInputPattern Input) x, (IMethodSymbol Method, TaintInputPattern Input) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Method, y.Method) && x.Input.Equals(y.Input);
        }

        public int GetHashCode((IMethodSymbol Method, TaintInputPattern Input) obj)
        {
            return HashCode.Combine(SymbolEqualityComparer.Default.GetHashCode(obj.Method), obj.Input.GetHashCode());
        }
    }
}
