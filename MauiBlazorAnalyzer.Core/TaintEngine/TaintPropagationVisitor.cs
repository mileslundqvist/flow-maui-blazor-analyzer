using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public class TaintPropagationVisitor : OperationVisitor<AnalysisState, AnalysisState>
{
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
        AnalysisState newState = state;

        for (int i = 0; i < operation.Arguments.Length; i++)
        {
            if (TaintPolicy.IsSink(operation, i, state))
            {
                // Add to vulnerability report list
                return state.SetTaint(operation.TargetMethod, TaintState.Tainted);
            }
        }

        if (TaintPolicy.IsSource(operation.TargetMethod))
        {
            // Need to identify where the return value goes (assignment target, etc.)
            // This usually happens *after* the invocation operation in the CFG.
            // The analysis might need to track return values separately.
            // For simplification here, let's assume we need a more complex analysis state...
            // Placeholder: Mark return value as tainted (requires better state representation)
            Console.WriteLine($"INFO: Taint source encountered: {operation.TargetMethod}");
        }

        // 3. Check if invocation is a Sanitizer
        else if (TaintPolicy.IsSanitizer(operation))
        {
            // Similar to source, need to identify where return value goes and mark it NOT tainted.
            // Placeholder: Mark return value as untainted.
            Console.WriteLine($"INFO: Sanitizer encountered: {operation.TargetMethod}");
        }
        // 4. Handle Inter-procedural Call (Complex!)
        else
        {
            bool anyArgTainted = operation.Arguments.Any(arg => GetOperationTaint(arg.Value, state) == TaintState.Tainted);
            if (anyArgTainted)
            {
            }
        }

        return newState;
    }


    private TaintState GetOperationTaint(IOperation operation, AnalysisState state)
    {
        if (operation == null) return TaintState.NotTainted;

        if (operation is IInvocationOperation localInvocation)
        {
            if (TaintPolicy.IsSource(localInvocation.TargetMethod))
            {
                return TaintState.Tainted;
            }
            else if (TaintPolicy.IsSanitizer(localInvocation))
            {
                return TaintState.NotTainted;
            }
        }

        if (operation is ILocalReferenceOperation localRef)
        {
            return state.GetTaint(localRef.Local);
        }

        return TaintState.NotTainted;
    }
}