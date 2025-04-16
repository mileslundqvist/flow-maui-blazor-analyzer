using MauiBlazorAnalyzer.Core.Analysis.CallGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public class ICFGProvider
{
    public InterproceduralCFG BuildICFG(Compilation compilation, CallGraph callGraph)
    {
        InterproceduralCFG ICFG = new InterproceduralCFG();

        foreach (var callerContext in callGraph.GetCallers())
        {
            if (!callerContext.MethodSymbol.ToDisplayString().Contains("Dangerous")) continue;

            var CFG = callerContext.ControlFlowGraph;
            if (CFG == null) continue;


            foreach (var block in CFG.Blocks)
            {

                for (int i = 0; i < block.Operations.Count() - 1; i++)
                {
                    var currentOp = block.Operations[i];
                    var nextOp = block.Operations[i + 1];
                    var fromNode = new ICFGNode(currentOp, callerContext);
                    var toNode = new ICFGNode(nextOp, callerContext);
                    ICFG.AddEdge(fromNode, toNode, EdgeType.Intraprocedural);
                }

                if (block.Operations.Any())
                {
                    var lastOperation = block.Operations.Last();
                    var fromNode = new ICFGNode(lastOperation, callerContext);

                    //foreach (var successorBlock in block.succ)
                }

                foreach (var operation in block.Operations)
                {
                    var callerNode = new ICFGNode(operation, callerContext);

                    if (operation is IExpressionStatementOperation expressionOperation)
                    {
                        var innerOperation = expressionOperation.Operation;

                        if (innerOperation is ISimpleAssignmentOperation simpleAssignmentOperation)
                        {
                            // This is a assignment, therefore the invocation must return a value
                            if (simpleAssignmentOperation.Value is IInvocationOperation invocationOperation)
                            {
                                // This method calls the following methods
                                var calleeContexts = callGraph.GetCallees(callerContext);

                                if (calleeContexts == null) continue;

                                // If the invocation is a call to one of these methods, then we add it to the graph
                                foreach (var calleeContext in calleeContexts)
                                {
                                    if (SymbolEqualityComparer.Default.Equals(invocationOperation.TargetMethod, calleeContext.MethodSymbol))
                                    {
                                        // The callee is the invocation that we are looping over
                                        var calleeCFG = calleeContext.ControlFlowGraph;
                                        if (calleeCFG == null)
                                            continue;

                                        //var calleeEntryOperation = calleeCFG.Blocks[0].Operations[0]; out of range

                                        //var calleeEntryNode = new ICFGNode(calleeEntryOperation, calleeContext);

                                    }
                                }
                            }

                        }

                        //if (innerOperation is IInvocationOperation invocationOperation)
                        //{

                        //}

                    }
                }
            }

        }

        return ICFG;
    }

    private IOperation GetReturnOperation(BasicBlock block, IOperation callOperation)
    {
        int index = block.Operations.IndexOf(callOperation);
        if (index >= 0 && index + 1 < block.Operations.Count())
        {
            return block.Operations[index + 1];
        }

        return null;
    }
}
