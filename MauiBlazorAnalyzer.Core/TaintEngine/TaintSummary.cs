using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public record TaintSummary(
    TaintState ReturnValueTaint,
    ImmutableDictionary<int, TaintState> ParameterTaintAtExit
)
{
    public static TaintSummary Unknown { get; } = new(TaintState.NotTainted, ImmutableDictionary<int, TaintState>.Empty);
    public static TaintSummary ConservativeAssumeTainted { get; } = new(TaintState.Tainted, ImmutableDictionary<int, TaintState>.Empty);
}
