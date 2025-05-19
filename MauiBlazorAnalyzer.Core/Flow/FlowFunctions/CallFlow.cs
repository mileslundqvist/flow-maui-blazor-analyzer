using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Flow;
using MauiBlazorAnalyzer.Core.Flow.DB;
using MauiBlazorAnalyzer.Core.Flow.FlowFunctions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Flow.FlowFunctions;
internal sealed class CallFlow : BaseFlowFunction
{


    public CallFlow(ICFGEdge edge, TaintSpecDB dB, List<EntryPointInfo> entryPoints) : base(edge, dB, entryPoints)
    {
    }


    /// <summary>
    /// Computes the set of facts holding true at the entry of the callee method,
    /// given a fact holding true at the call site before the call.
    /// Primarily maps tainted actual arguments to formal parameters.
    /// </summary>
    /// <param name="inFactAtCallSite">The fact holding true before the call (can be ZeroFact or TaintFact).</param>
    /// <returns>A set of facts holding true at the callee's entry point.</returns>
    public override ISet<IFact> ComputeTargets(IFact inFactAtCallSite)
    {
        var outFacts = new HashSet<IFact>();


        // --- Step 3: Extract Call Information from the Call Site Operation ---
        if (!TryGetCallInfo(Edge.From.Operation, out var calleeMethod, out var arguments))
        {
            return outFacts;
        }

        // --- Step 4: Map Tainted Arguments to Callee Parameters ---
        // Check if the incoming taint fact applies to any of the actual argument values.
        // If yes, create a new fact for the corresponding formal parameter.
        MapTaintedArgumentsToParameters(inFactAtCallSite, arguments, outFacts);


        //// Introduce taint if the callee is a source
        if (DB.IsSource(calleeMethod))
        {
            outFacts.Add(new TaintFact(calleeMethod));
        }

        return outFacts;
    }



    /// <summary>
    /// Attempts to extract the callee method symbol and argument operations
    /// from the IOperation associated with a call site node.
    /// </summary>
    /// <param name="callSiteOperation">The operation at the call site node.</param>
    /// <param name="calleeMethod">Output: The target method or constructor symbol.</param>
    /// <param name="arguments">Output: The arguments passed in the call.</param>
    /// <returns>True if call information was successfully extracted, false otherwise.</returns>
    private bool TryGetCallInfo(IOperation? callSiteOperation, out IMethodSymbol? calleeMethod, out IEnumerable<IArgumentOperation> arguments)
    {
        calleeMethod = null;
        arguments = Enumerable.Empty<IArgumentOperation>();

        if (callSiteOperation == null) return false;

        // Pattern match for different ways a call can appear
        switch (callSiteOperation)
        {
            case IInvocationOperation inv:
                calleeMethod = inv.TargetMethod;
                arguments = inv.Arguments;
                return calleeMethod != null;

            case IObjectCreationOperation obj:
                calleeMethod = obj.Constructor;
                arguments = obj.Arguments;
                return calleeMethod != null;

            // Assignment where RHS is the call (e.g., x = Method())
            case ISimpleAssignmentOperation { Value: IInvocationOperation assignInv }:
                calleeMethod = assignInv.TargetMethod;
                arguments = assignInv.Arguments;
                return calleeMethod != null;

            case ISimpleAssignmentOperation { Value: IObjectCreationOperation assignObj }:
                calleeMethod = assignObj.Constructor;
                arguments = assignObj.Arguments;
                return calleeMethod != null;
            case IExpressionStatementOperation expressionStatement:
                switch (expressionStatement.Operation)
                {
                    case IInvocationOperation exprInv:
                        calleeMethod = exprInv.TargetMethod;
                        arguments = exprInv.Arguments;
                        return calleeMethod != null;
                    default:
                        return false;
                }
            default:
                return false; // Operation is not a recognized call pattern
        }
    }

    /// <summary>
    /// Maps the incoming taint fact (representing a tainted argument value at the call site)
    /// to the corresponding callee parameter(s), adding new TaintFacts for the parameters to the output set.
    /// </summary>
    private void MapTaintedArgumentsToParameters(IFact callSiteFact, IEnumerable<IArgumentOperation> arguments, ISet<IFact> outFacts)
    {
        foreach (var arg in arguments)
        {

            if (callSiteFact is TaintFact callSiteTaintFact)
            {
                // Does the incoming fact represent the value passed to this specific argument?
                // And does this argument map to a formal parameter?
                if (arg.Parameter != null && callSiteTaintFact.AppliesTo(arg.Value))
                {
                    // Yes: Create a new fact representing the *parameter* being tainted.
                    outFacts.Add(callSiteTaintFact.WithNewBase(arg.Parameter));
                }
            }
            else
            {
                // Is the ZeroFact actually a bound-variable in disguise?
                foreach (var entryPoint in EntryPoints)
                {
                    if (entryPoint.Type != EntryPointType.BindingCallback || entryPoint.EntryPointSymbol == null || entryPoint.AssociatedSymbol == null)
                        continue;

                    var valueSymbol = GetOperationSymbol(arg.Value);

                    if (arg.Parameter != null
                        && valueSymbol != null
                        && SymbolEqualityComparer.Default.Equals(valueSymbol, entryPoint.AssociatedSymbol))
                    {
                        var targetPath = new AccessPath(arg.Parameter, ImmutableArray<IFieldSymbol>.Empty);
                        var targetTaintFact = new TaintFact(targetPath);
                        outFacts.Add(targetTaintFact);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Helper to get the base ISymbol from common reference operations.
    /// </summary>
    private ISymbol? GetOperationSymbol(IOperation operation)
    {
        while (operation is IConversionOperation conv) { operation = conv.Operand; }

        return operation switch
        {
            ILocalReferenceOperation loc => loc.Local,
            IParameterReferenceOperation parm => parm.Parameter,
            IFieldReferenceOperation fld => fld.Field,
            IPropertyReferenceOperation prop => prop.Property,
            IInstanceReferenceOperation => null,
            _ => null // Cannot determine symbol for other operation types
        };
    }
}
