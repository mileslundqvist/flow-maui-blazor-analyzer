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

        if (TaintPolicy.IsSink(operation.TargetMethod.ToDisplayString()))
        {
            foreach (var argument in operation.Arguments)
            {
                IOperation argumentValueOperation = argument.Value;
                TaintState valueTaint = GetOperationTaint(argumentValueOperation, newState);

                if (valueTaint == TaintState.Tainted)
                {
                    newState = newState.SetTaint(operation.TargetMethod, valueTaint);
                }

            }
        } else if (TaintPolicy.IsSource(operation.TargetMethod.ToDisplayString()))
        {

        } else
        {

        }


        return newState;
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