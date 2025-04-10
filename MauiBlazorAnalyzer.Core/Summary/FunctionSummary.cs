using MauiBlazorAnalyzer.Core.TaintEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Summary;

public class FunctionSummary
{
    public int KillMask { get; }
    public int GenMask { get; }

    public FunctionSummary(int killMask, int genMask)
    {
        KillMask = killMask;
        GenMask = genMask;
    }

    public int Apply(int state)
    {
        return (state & ~KillMask) | GenMask;
    }

    public override string ToString()
    {
        return $"Kill: {Convert.ToString(KillMask, 2)}, Gen: {Convert.ToString(GenMask, 2)}";
    }
}
