using MauiBlazorAnalyzer.Core.Analysis.Taint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;

public enum EdgeType { Intraprocedural, Call, Return, CallToReturn }

public class ICFGEdge
{
    public ICFGNode From { get; }
    public ICFGNode To { get; }
    public EdgeType Type { get; }

    public ICFGEdge(ICFGNode from, ICFGNode to, EdgeType type)
    {
        From = from;
        To = to;
        Type = type;
    }
}
