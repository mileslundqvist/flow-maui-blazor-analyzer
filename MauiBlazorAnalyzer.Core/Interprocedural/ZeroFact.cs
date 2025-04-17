using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public sealed record ZeroFact : IFact
{
    public static readonly ZeroFact Instance = new ZeroFact();
    private ZeroFact() { }
}
