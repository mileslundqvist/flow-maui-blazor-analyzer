using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public static class TaintPolicy
{
    public static bool IsSource(IMethodSymbol methodSymbol)
    {
        // TODO: Implement a more comprehensive check for sources
        return methodSymbol.ToDisplayString().Contains("Console.ReadLine") ||
               methodSymbol.ToDisplayString().Contains("HttpContext.Request.Query") ||
               methodSymbol.ToDisplayString().Contains("HttpContext.Request.Form") ||
               methodSymbol.ToDisplayString().Contains("HttpContext.Request.Body");
    }

    public static bool IsSink(IInvocationOperation invocation, int argumentIndex, AnalysisState currentState)
    {
        // TODO: Implement a more comprehensive check for sinks
        if (invocation.TargetMethod.ToString().Contains("Console.WriteLine") && argumentIndex == 0)
        {
            var argument = invocation.Arguments[argumentIndex].Value;

            if (argument is ILocalReferenceOperation localRef && currentState.GetTaint(localRef.Local) == TaintState.Tainted) return true;
            if (argument is IParameterReferenceOperation paramRef && currentState.GetTaint(paramRef.Parameter) == TaintState.Tainted) return true;
        }
        return false;
    }

    public static bool IsSanitizer(IInvocationOperation invocation)
    {
        // TODO: Implement a more comprehensive check for sanitizers
        return invocation.TargetMethod.ToString().Contains("MySanitizer.Sanitize");
    }
}
