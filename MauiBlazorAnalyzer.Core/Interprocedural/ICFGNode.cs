using MauiBlazorAnalyzer.Core.Intraprocedural.Context;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;

public enum ICFGNodeKind { Normal, CallSite, Entry, Exit, ReturnSite }

public class ICFGNode
{
    public IOperation Operation { get; }
    public MethodAnalysisContext MethodContext { get; }
    public ICFGNodeKind Kind { get; }
    

    public ICFGNode(IOperation operation, MethodAnalysisContext context, ICFGNodeKind kind = ICFGNodeKind.Normal)
    {
        Operation = operation;
        MethodContext = context;
        Kind = kind;
    }
}
