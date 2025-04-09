using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using MauiBlazorAnalyzer.Core.CallGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class TaintPropagationVisitor : OperationVisitor<AnalysisState, AnalysisState>
{
    private readonly CallGraph.CallGraph _callGraph;
    private readonly SummaryManager _summaryManager;
    private readonly TaintEngine _engine;
    public TaintPropagationVisitor(CallGraph.CallGraph callGraph, SummaryManager summaryManager, TaintEngine engine)
    {
        _callGraph = callGraph ?? throw new ArgumentNullException(nameof(callGraph));
        _summaryManager = summaryManager ?? throw new ArgumentNullException(nameof(summaryManager));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }
    public override AnalysisState DefaultVisit(IOperation operation, AnalysisState state)
    {
        // Default: Assume operations don't change taint state unless overridden
        return state;
    }

    public override AnalysisState? VisitSimpleAssignment(ISimpleAssignmentOperation operation, AnalysisState state)
    {
        var valueTaint = GetOperationTaint(operation.Value, state);

        if (operation.Target is ILocalReferenceOperation localTarget)
        {
            // If the target is a local variable, set its taint state based on the value's taint
            return state.SetTaint(localTarget.Local, valueTaint);
        }
        else if (operation.Target is IFieldReferenceOperation fieldTarget)
        {
            // If the target is a field, set its taint state based on the value's taint
            return state.SetTaint(fieldTarget.Field, valueTaint);
        }
        else if (operation.Target is IPropertyReferenceOperation propertyTarget)
        {
            // If the target is a property, set its taint state based on the value's taint
            return state.SetTaint(propertyTarget.Property, valueTaint);
        }
        // If the target is not a local variable, field, or property, return the state unchanged
        return state;
    }

    public override AnalysisState? VisitExpressionStatement(IExpressionStatementOperation operation, AnalysisState state)
    {
        IOperation innerOperation = operation.Operation;
        AnalysisState stateAfterInner = innerOperation.Accept(this, state);

        return stateAfterInner;

    }

    public override AnalysisState? VisitInvocation(IInvocationOperation operation, AnalysisState state)
    {
        AnalysisState currentState = state;
        AnalysisState stateAfterCall = state;
        IMethodSymbol? calleeMethodSymbol = operation.TargetMethod;

        if (TaintPolicy.IsSink(operation.TargetMethod.ToDisplayString()))
        {
            foreach (var argument in operation.Arguments)
            {
                IOperation argumentValueOperation = argument.Value;
                TaintState valueTaint = GetOperationTaint(argumentValueOperation, currentState);

                if (valueTaint == TaintState.Tainted)
                {
                    stateAfterCall = currentState.SetTaint(operation.TargetMethod, valueTaint);
                }

            }
        } else if (TaintPolicy.IsSource(operation.TargetMethod.ToDisplayString()))
        {

        } else
        {
            // Here we have a interprocedural call that we want to explore
            Console.WriteLine($"Exploring interprocedural call to {operation.TargetMethod.ToDisplayString()}");


            // Check if the arguments are tainted
            var taintedIndices = ImmutableHashSet.CreateBuilder<int>();
            for (int i = 0; i < operation.Arguments.Length; i++)
            {
                IOperation argumentValueOperation = operation.Arguments[i].Value;
                TaintState valueTaint = GetOperationTaint(argumentValueOperation, currentState);

                if (valueTaint == TaintState.Tainted)
                {
                    taintedIndices.Add(i);
                }
            }

            TaintInputPattern inputPattern = new(taintedIndices.ToImmutable());

            if (!_summaryManager.TryGetSummary(calleeMethodSymbol, inputPattern, out var summary))
            {
                // 3. Compute Summary (Cache Miss) - delegate to engine
                summary = _engine.ComputeSummary(calleeMethodSymbol, inputPattern);
                // 4. Store Summary
                _summaryManager.StoreSummary(calleeMethodSymbol, inputPattern, summary);
            }

            stateAfterCall = ApplySummaryOutput(operation, summary, currentState);
        }


        return stateAfterCall;
    }

    private AnalysisState ApplySummaryOutput(IInvocationOperation operation, TaintSummary summary, AnalysisState stateBefore)
    {
        TaintState returnValueTaint = summary.ReturnValueTaint;
        AnalysisState newState = stateBefore;
        

        return stateBefore;
    }

    private TaintState GetOperationTaint(IOperation operation, AnalysisState state)
    {
        if (operation == null) return TaintState.NotTainted;

        if (operation is IInvocationOperation localInvocation)
        {
            if (TaintPolicy.IsSource(localInvocation.TargetMethod.ToDisplayString()))
            {
                return TaintState.Tainted;
            }
            else if (TaintPolicy.IsSanitizer(localInvocation.TargetMethod.ToDisplayString()))
            {
                return TaintState.NotTainted;
            }
        }

        if (operation is ILocalReferenceOperation localRef)
        {
            return state.GetTaint(localRef.Local);
        }

        if (operation is IParameterReferenceOperation parameterRef)
        {
            return state.GetTaint(parameterRef.Parameter);
        }

        return TaintState.NotTainted;
    }
}