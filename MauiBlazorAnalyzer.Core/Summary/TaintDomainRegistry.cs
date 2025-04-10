using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Summary;

public static class TaintDomainRegistry
{
    private static readonly Dictionary<string, TaintFact> _facts = new();
    private static int _nextIndex = 0;

    public static TaintFact GetOrCreateFact(string factName)
    {
        if (!_facts.TryGetValue(factName, out var fact))
        {
            fact = new TaintFact(_nextIndex++, factName);
            _facts[factName] = fact;
        }
        return fact;
    }

    public static IReadOnlyCollection<TaintFact> GetAllFacts() => _facts.Values;
}
