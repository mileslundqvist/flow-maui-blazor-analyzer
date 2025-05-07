using MauiBlazorAnalyzer.Core.EntryPoints;
using MauiBlazorAnalyzer.Core.Interprocedural.DB;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace MauiBlazorAnalyzer.Core.Interprocedural.FlowFunctions;
internal sealed class NormalFlow : BaseFlowFunction
{
    public NormalFlow(ICFGEdge edge, TaintSpecDB db, List<EntryPointInfo> entryPoints) : base(edge, db, entryPoints) { }

    public override ISet<IFact> ComputeTargets(IFact inFact)
    {
        // Now we need to handle the IFact, because a Taint can be introduced from a source or by being a bind-variable
        var outFacts = new HashSet<IFact>();
        var operation = Edge.From.Operation;
        var containingMethod = Edge.From.MethodContext.MethodSymbol;


        // --- Step 1: Taint Generation ---
        // Check if the current operation introduces *new* taint, regardless of inFact.
        GenerateNewTaint(operation, containingMethod, outFacts);


        // --- Step 2: Taint Propagation and Killing ---
        // Only applies if the incoming fact represents existing taint.
        if (inFact is TaintFact currentTaintFact)
        {
            PropagateOrKillIncomingTaint(currentTaintFact, operation, outFacts);
        }

        if (!outFacts.Any())
        {
            outFacts.Add(inFact);
           
        }

        return outFacts;
    }

    /// <summary>
    /// Checks if the operation generates new taint (e.g., from bindings or known sources)
    /// and adds corresponding TaintFacts to the output set.
    /// </summary>
    private void GenerateNewTaint(IOperation? operation, IMethodSymbol? containingMethod, ISet<IFact> outFacts)
    {
        if (operation == null) return;

        // 1a. Binding Callback Generation
        if (operation is ISimpleAssignmentOperation bindingAssign && containingMethod != null)
        {
            GenerateTaintFromBindingCallback(bindingAssign, containingMethod, outFacts);
        }

        // 1b. Standard Source Generation (e.g., calling a known source method)
        if (operation is IInvocationOperation sourceInvocation)
        {
            GenerateTaintFromSourceInvocation(sourceInvocation, outFacts);
        }
        else if (operation is ISimpleAssignmentOperation assignmentWithSource)
        {
            // Handle cases like x = SourceMethod();
            if (assignmentWithSource.Value is IInvocationOperation assignedSourceInv)
            {
                GenerateTaintFromSourceInvocation(assignedSourceInv, outFacts, assignmentWithSource.Target);
            }
        }
        // TODO: Add checks for other types of operations that might be sources (e.g., Property/Field reads)
    }


    /// <summary>
    /// Checks if a specific assignment matches a Blazor BindingCallback and generates taint if it does.
    /// </summary>
    private void GenerateTaintFromBindingCallback(ISimpleAssignmentOperation assignmentOp, IMethodSymbol containingMethod, ISet<IFact> outFacts)
    {
        foreach (var entryPoint in EntryPoints)
        {
            if (entryPoint.Type != EntryPointType.BindingCallback || entryPoint.EntryPointSymbol == null || entryPoint.AssociatedSymbol == null)
                continue;
      
            var assignmentValueSymbol = GetOperationSymbol(assignmentOp.Value);
            var assignmentTargetSymbol = GetOperationSymbol(assignmentOp.Target);

            if (assignmentTargetSymbol != null && SymbolEqualityComparer.Default.Equals(assignmentValueSymbol, entryPoint.AssociatedSymbol))
            {
                // FOUND BINDING ASSIGNMENT: Generate NEW taint for the symbols
                var targetPath = new AccessPath(assignmentTargetSymbol, ImmutableArray<IFieldSymbol>.Empty);
                var targetTaintFact = new TaintFact(targetPath);
                outFacts.Add(targetTaintFact);


                var boundVariablePath = new AccessPath(assignmentTargetSymbol, ImmutableArray<IFieldSymbol>.Empty);
                var boundTaintFact = new TaintFact(boundVariablePath);
                outFacts.Add(boundTaintFact);

                // Found the specific assignment, no need to check other entry points for this operation
                return;
            }
            
        }
    }

    /// <summary>
    /// Checks if an invocation operation calls a known source and generates taint accordingly.
    /// Can optionally taint the target operation if the source result is assigned.
    /// </summary>
    private void GenerateTaintFromSourceInvocation(IInvocationOperation invocationOp, ISet<IFact> outFacts, IOperation? assignmentTarget = null)
    {
        if (DB.IsSource(invocationOp.TargetMethod))
        {
            if (assignmentTarget != null)
            {
                // Taint the LHS of the assignment
                var targetSymbol = GetOperationSymbol(assignmentTarget);
                if (targetSymbol != null)
                {
                    var path = new AccessPath(targetSymbol, ImmutableArray<IFieldSymbol>.Empty);
                    outFacts.Add(new TaintFact(path));
                }
            }
            else
            {
                // Taint the return value itself
                outFacts.Add(new TaintFact(invocationOp.TargetMethod));
            }
        }
    }

    /// <summary>
    /// Handles the propagation or killing of an existing incoming TaintFact based on the operation.
    /// Adds resulting facts (pass-through, propagated, or modified) to the output set.
    /// </summary>
    private void PropagateOrKillIncomingTaint(TaintFact currentTaintFact, IOperation? operation, ISet<IFact> outFacts)
    {
        if (operation == null)
        {
            outFacts.Add(currentTaintFact); // Pass through if operation is unknown
            return;
        }

        bool killed = IsTaintKilled(currentTaintFact, operation);

        if (!killed)
        {
            PropagateTaint(currentTaintFact, operation, outFacts);
        }
        // If killed, we simply don't add currentTaintFact or its successors to outFacts.
    }

    /// <summary>
    /// Determines if the incoming taint fact is killed by the operation.
    /// </summary>
    private bool IsTaintKilled(TaintFact currentTaintFact, IOperation operation)
    {
        // Kill Logic Example: Assignment overwrites the target completely
        if (operation is ISimpleAssignmentOperation killAssign)
        {
            var targetSymbol = GetOperationSymbol(killAssign.Target);
            // Check if the incoming fact corresponds *exactly* to the variable being assigned
            // This check might need refinement based on AccessPath (e.g., field access)
            if (targetSymbol != null && currentTaintFact.Path?.Base != null &&
                SymbolEqualityComparer.Default.Equals(targetSymbol, currentTaintFact.Path.Base) &&
                currentTaintFact.Path.Fields.IsEmpty)
            {
                return true; // Taint of the variable being assigned is killed
            }
        }

        return false;
    }


    /// <summary>
    /// Determines how the incoming taint fact propagates through the operation
    /// and adds the resulting facts to the output set.
    /// </summary>
    private void PropagateTaint(TaintFact currentTaintFact, IOperation operation, ISet<IFact> outFacts)
    {
        // --- Propagation Logic ---

        // 1. Assignment: Propagate from RHS to LHS
        if (operation is ISimpleAssignmentOperation genAssign)
        {
            if (currentTaintFact.AppliesTo(genAssign.Value)) // Does inFact represent the RHS value?
            {
                var targetSymbol = GetOperationSymbol(genAssign.Target);
                if (targetSymbol != null)
                {
                    // Propagate taint to LHS symbol
                    outFacts.Add(currentTaintFact.WithNewBase(targetSymbol));
                }
            }
            else
            {
                // Pass through unrelated taint
                outFacts.Add(currentTaintFact);
            }
            return;
        }

        // 2. Return Statement: Propagate to ReturnValue fact
        if (operation is IReturnOperation returnOp && returnOp.ReturnedValue != null)
        {
            if (currentTaintFact.AppliesTo(returnOp.ReturnedValue)) // Does inFact represent the returned value?
            {
                var containingMethod = Edge.From.MethodContext?.MethodSymbol;
                if (containingMethod != null)
                {
                    outFacts.Add(new TaintFact(containingMethod));
                }
                // else: Cannot create return fact without method symbol
            }
            else
            {
                outFacts.Add(currentTaintFact); // Pass through unrelated taint
            }
            return; // Handled return case
        }

        // TODO: Add propagation for:
        // - Field/Property Access (e.g., if `x` is tainted, `y = x.F` taints `y`, potentially with path `x.F`)
        // - Method arguments (handled by CallFlow, but intraprocedural uses might need handling)
        // - Array access
        // - String concatenations, etc.

        outFacts.Add(currentTaintFact);
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
            IPropertyReferenceOperation prop => prop.Property, // Usually the property symbol itself
            IInstanceReferenceOperation => null, // 'this' usually isn't the root of taint path directly
            _ => null // Cannot determine symbol for other operation types
        };
    }
}
