using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;

public interface ITaintRule
{
    string Name { get; }
    string Severity { get; }
    bool Matches(string methodSignature);
    bool Matches(ISymbol symbol);
}
