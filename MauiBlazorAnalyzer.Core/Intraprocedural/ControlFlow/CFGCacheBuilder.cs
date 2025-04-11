using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Intraprocedural.ControlFlow;
public static class CFGCacheBuilder
{
    public static CFGCache BuildCFGCache(IEnumerable<IOperation> operations)
    {
        foreach (IOperation operation in operations)
        {
            ControlFlowGraph? controlFlowGraph;
            IMethodSymbol methodSymbol;

            if (operation is IMethodBodyOperation methodBodyOperation)
            {
                controlFlowGraph = ControlFlowGraph.Create(methodBodyOperation);
            }

            if (operation is IConstructorBodyOperation constructorBodyOperation)
            {
                controlFlowGraph = ControlFlowGraph.Create(constructorBodyOperation);
            }


        }



        return new CFGCache();
    }
}
