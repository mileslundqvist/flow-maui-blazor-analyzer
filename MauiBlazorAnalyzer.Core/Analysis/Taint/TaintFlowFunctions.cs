//using Microsoft.CodeAnalysis.Operations;
//using Microsoft.CodeAnalysis;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using MauiBlazorAnalyzer.Core.Interprocedural;

//namespace MauiBlazorAnalyzer.Core.Analysis.Taint;
//public class TaintFlowFunctions : IFlowFunctions
//{
//    private readonly Compilation _compilation;

//    private static readonly HashSet<string> KnownTaintSources = new() { "System.Console.ReadLine()", "YourNamespace.GetSensitiveData()" };
//    private static readonly HashSet<string> KnownSinks = new() { "System.Console.WriteLine(string?)", "YourNamespace.SendData(string)" };
//    private static readonly HashSet<string> KnownSanitizers = new() { "YourNamespace.Sanitize(string)" };
//    public TaintFlowFunctions(Compilation compilation) { _compilation = compilation; }

//    public IFlowFunction GetNormalFlowFunction(IOperation currentNode, IOperation? successorNode)
//    {
//        return new LambdaFlowFunction(sourceFact =>
//        {
//            var results = new HashSet<IFact>();

//            if (sourceFact is ZeroFact)
//            {
//                results.Add(ZeroFact.Instance);
//                HandleZeroFactGeneration(currentNode, results);
//            }
//            else if (sourceFact is TaintFact taintFact)
//            {
//                HandleTaintFactPropagation(currentNode, taintFact, results);
//            }

//            return results;
//        });
//    }

//    private void HandleZeroFactGeneration(IOperation operation, HashSet<IFact> results)
//    {
//        if (operation is IInvocationOperation invocation && IsTaintSource(invocation.TargetMethod))
//        {
//            ISymbol? target = GetAssignmentTargetSymbol(operation);
//            if (target != null) results.Add(new TaintFact(target));
//        }
//        else if (operation is IAssignmentOperation assignment && assignment.Value is IInvocationOperation sourceInvocation && IsTaintSource(sourceInvocation.TargetMethod))
//        {
//            ISymbol? target = GetOperationTargetSymbol(assignment.Target);
//            if (target != null) results.Add(new TaintFact(target));
//        }
//    }

//    private void HandleTaintFactPropagation(IOperation operation, TaintFact currentTaintFact, HashSet<IFact> results)
//    {
//        ISymbol taintedSymbol = currentTaintFact.TaintedSymbol;
//        bool killed = false;
//        bool propagated = false;

//        switch (operation)
//        {
//            case IAssignmentOperation assignment:
//                ISymbol? target = GetOperationTargetSymbol(assignment.Target);
//                ISymbol? sourceValue = GetOperationValueSymbol(assignment.Value);

//                if (target != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, target))
//                {
//                    killed = true; // Assignment kills previous taint on target
//                }

//                if (!killed && target != null && sourceValue != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, sourceValue))
//                {
//                    results.Add(new TaintFact(target)); // Propagate taint: target = tainted source
//                    propagated = true;
//                }
//                // Handle case where assignment value itself generates taint (e.g., x = Source())
//                // This case is tricky - should it add taint even if sourceFact wasn't Zero? Depends on analysis goal.
//                // Usually handled by HandleZeroFactGeneration adding the taint for the target symbol.

//                // Handle assignment of sanitizer result: target = Sanitize(tainted source)
//                if (target != null && assignment.Value is IInvocationOperation sanitizerInvocation &&
//                   IsSanitizer(sanitizerInvocation.TargetMethod))
//                {
//                    ISymbol? sanitizerArg = GetOperationValueSymbol(sanitizerInvocation.Arguments.FirstOrDefault()?.Value);
//                    if (sanitizerArg != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, sanitizerArg))
//                    {
//                        // Taint is killed by the sanitizer, don't propagate to target, don't add original fact.
//                        killed = true;
//                        propagated = true; // Mark as handled, prevents default propagation
//                    }
//                }
//                break;

//            case IInvocationOperation invocation:
//                // Check if calling a sink with a tainted argument
//                if (IsSink(invocation.TargetMethod))
//                {
//                    foreach (var argument in invocation.Arguments)
//                    {
//                        ISymbol? argSymbol = GetOperationValueSymbol(argument.Value);
//                        if (argSymbol != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, argSymbol))
//                        {
//                            Console.WriteLine($"Potential Leak: Tainted data {taintedSymbol.Name} passed to sink {invocation.TargetMethod.Name} at {invocation.Syntax.GetLocation().GetMappedLineSpan()}");
//                            // Note: Reporting is often done separately, but shown here for illustration.
//                            // Taint still propagates past the sink call unless the sink modifies it.
//                        }
//                    }
//                }
//                // Check if calling a method known to propagate taint (e.g., string concat)
//                // result = String.Concat(taintedSymbol, other); -> taint result
//                // Complex: requires modeling specific methods or using summaries.
//                break;

//            case IBinaryOperation binaryOperation:
//                // Propagate taint if either operand is tainted (simplistic view)
//                ISymbol? leftOperand = GetOperationValueSymbol(binaryOperation.LeftOperand);
//                ISymbol? rightOperand = GetOperationValueSymbol(binaryOperation.RightOperand);
//                ISymbol? assignmentTarget = GetAssignmentTargetSymbol(binaryOperation); // Find where result is stored

//                if (assignmentTarget != null)
//                {
//                    if ((leftOperand != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, leftOperand)) ||
//                        (rightOperand != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, rightOperand)))
//                    {
//                        results.Add(new TaintFact(assignmentTarget));
//                        propagated = true;
//                    }
//                }
//                break;

//                // Add cases for other relevant IOperation types:
//                // IFieldReferenceOperation, IPropertyReferenceOperation, IReturnOperation, etc.
//        }

//        // Default propagation: If taint wasn't killed or explicitly propagated differently, it persists.
//        if (!killed && !propagated)
//        {
//            results.Add(currentTaintFact);
//        }
//    }
//    public IFlowFunction GetCallFlowFunction(IOperation callSite, IMethodSymbol calledMethod)
//    {
//        return new LambdaFlowFunction(sourceFact =>
//        {
//            var results = new HashSet<IFact>();
//            if (sourceFact is ZeroFact)
//            {
//                results.Add(ZeroFact.Instance);
//                // Handle generation based on call if needed (e.g., factory returning tainted obj)
//            }
//            else if (sourceFact is TaintFact taintFact)
//            {
//                ISymbol taintedSymbol = taintFact.TaintedSymbol;

//                if (callSite is IInvocationOperation invocation)
//                {
//                    // Map instance receiver
//                    if (!calledMethod.IsStatic && invocation.Instance != null)
//                    {
//                        ISymbol? receiverSymbol = GetOperationValueSymbol(invocation.Instance);
//                        if (receiverSymbol != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, receiverSymbol))
//                        {
//                            if (calledMethod.ReceiverType != null) // 'this' doesn't have a formal parameter symbol easily accessible? Check this.
//                            {
//                                // Need a way to represent 'this' inside the callee context.
//                                // Often IFDS implementations use a special 'this' fact or rely on alias analysis.
//                                // Skipping precise 'this' mapping for simplicity here.
//                            }
//                        }
//                    }

//                    // Map arguments
//                    for (int i = 0; i < invocation.Arguments.Length && i < calledMethod.Parameters.Length; i++)
//                    {
//                        ISymbol? actualArgSymbol = GetOperationValueSymbol(invocation.Arguments[i].Value);
//                        if (actualArgSymbol != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, actualArgSymbol))
//                        {
//                            results.Add(new TaintFact(calledMethod.Parameters[i]));
//                        }
//                    }
//                }
//                else if (callSite is IObjectCreationOperation objectCreation)
//                {
//                    // Map arguments for constructors
//                    for (int i = 0; i < objectCreation.Arguments.Length && i < calledMethod.Parameters.Length; i++)
//                    {
//                        ISymbol? actualArgSymbol = GetOperationValueSymbol(objectCreation.Arguments[i].Value);
//                        if (actualArgSymbol != null && SymbolEqualityComparer.Default.Equals(taintedSymbol, actualArgSymbol))
//                        {
//                            results.Add(new TaintFact(calledMethod.Parameters[i]));
//                        }
//                    }
//                }
//                // Add ZeroFact if it wasn't already added? IFDS usually ensures Zero propagation.
//                if (!results.Any()) results.Add(ZeroFact.Instance); // Ensure Zero if no taint maps
//            }
//            return results;
//        });
//    }

//    public IFlowFunction GetReturnFlowFunction(IOperation callSite, IMethodSymbol calledMethod, IOperation exitNode, IOperation returnSite)
//    {
//        // This function maps facts from the exit of the callee back to the caller context.
//        // It needs access to facts holding *before* the call site ('callerSourceFacts')
//        // which the IFDS solver typically provides alongside the 'exitFact'.
//        return new LambdaFlowFunction(exitFact =>
//        {
//            var results = new HashSet<IFact>();

//            // 1. Propagate ZeroValue from callee exit AND unaffected caller facts
//            if (exitFact is ZeroFact)
//            {
//                results.Add(ZeroFact.Instance);
//                // Need logic here to add callerSourceFacts that are not killed by the call's side effects.
//                // This usually requires knowing which globals/fields the callee *might* modify.
//                // Simplified: Assume only direct assignment target is affected for now.
//            }
//            // 2. Handle tainted return value from callee
//            else if (exitFact is TaintFact taintFact)
//            {
//                ISymbol taintedSymbolAtExit = taintFact.TaintedSymbol;
//                ISymbol? returnedSymbol = GetReturnedSymbol(exitNode); // Symbol returned by callee

//                if (returnedSymbol != null && SymbolEqualityComparer.Default.Equals(taintedSymbolAtExit, returnedSymbol))
//                {
//                    ISymbol? assignmentTarget = GetAssignmentTargetAtReturnSite(callSite); // Symbol receiving result in caller
//                    if (assignmentTarget != null)
//                    {
//                        results.Add(new TaintFact(assignmentTarget));
//                    }
//                }
//                // 3. Handle taint on parameters passed by ref/out (if applicable)
//                // TODO

//                // 4. Handle taint on fields/globals modified by callee (needs summaries/alias analysis)
//                // TODO

//                // Add ZeroFact if no specific taint propagated back? Needs careful definition.
//                if (!results.Any()) results.Add(ZeroFact.Instance);
//            }

//            return results;
//        });
//    }

//    public IFlowFunction GetCallToReturnFlowFunction(IOperation callSite, IOperation returnSite)
//    {
//        // Propagates facts that are unaffected by the call.
//        // Example: If variable 'z' is tainted before 'x = foo(y)', and 'foo' doesn't
//        // affect 'z', then 'z' should still be tainted after the call.
//        // Requires knowing side effects. Simple approximation is identity.
//        return new LambdaFlowFunction(sourceFact =>
//        {
//            var results = new HashSet<IFact>();
//            // More accurate: Check if sourceFact relates to symbols potentially modified by the call.
//            results.Add(sourceFact);
//            return results;
//        });
//    }

//    // --- Helper Methods --- (Ensure these are robust)
//    private bool IsTaintSource(IMethodSymbol methodSymbol)
//    {
//        return KnownTaintSources.Contains(methodSymbol.OriginalDefinition.ToDisplayString());
//    }

//    private bool IsSink(IMethodSymbol methodSymbol)
//    {
//        return KnownSinks.Contains(methodSymbol.OriginalDefinition.ToDisplayString());
//    }
//    private bool IsSanitizer(IMethodSymbol methodSymbol)
//    {
//        return KnownSanitizers.Contains(methodSymbol.OriginalDefinition.ToDisplayString());
//    }


//    private ISymbol? GetAssignmentTargetSymbol(IOperation operation)
//    {
//        // Tries to find the LValue symbol for an assignment or initializer containing 'operation'
//        if (operation.Parent is IAssignmentOperation assignment && assignment.Value == operation)
//        {
//            return GetOperationTargetSymbol(assignment.Target);
//        }
//        if (operation.Parent is IFieldInitializerOperation fieldInit && fieldInit.Value == operation)
//        {
//            return fieldInit.InitializedFields.FirstOrDefault();
//        }
//        if (operation.Parent is IPropertyInitializerOperation propInit && propInit.Value == operation)
//        {
//            return propInit.InitializedProperties.FirstOrDefault();
//        }
//        // If operation itself is the target
//        if (operation is ILocalReferenceOperation localRef) return localRef.Local;
//        if (operation is IParameterReferenceOperation paramRef) return paramRef.Parameter;
//        if (operation is IFieldReferenceOperation fieldRef) return fieldRef.Field;
//        if (operation is IPropertyReferenceOperation propRef) return propRef.Property;

//        return null;
//    }


//    private ISymbol? GetOperationTargetSymbol(IOperation? target)
//    {
//        if (target is ILocalReferenceOperation localRef) return localRef.Local;
//        if (target is IParameterReferenceOperation paramRef) return paramRef.Parameter;
//        if (target is IFieldReferenceOperation fieldRef) return fieldRef.Field;
//        if (target is IPropertyReferenceOperation propRef) return propRef.Property;
//        if (target is IInstanceReferenceOperation) return null; // Represent 'this' differently if needed
//        return null;
//    }

//    private ISymbol? GetOperationValueSymbol(IOperation? value)
//    {
//        if (value is IConversionOperation conv) return GetOperationValueSymbol(conv.Operand); // Look through conversions

//        if (value is ILocalReferenceOperation localRef) return localRef.Local;
//        if (value is IParameterReferenceOperation paramRef) return paramRef.Parameter;
//        if (value is IFieldReferenceOperation fieldRef) return fieldRef.Field;
//        if (value is IPropertyReferenceOperation propRef) return propRef.Property;
//        if (value is IInstanceReferenceOperation) return null; // Represent 'this' differently if needed
//                                                               // Literals, method calls that return values etc don't have a simple symbol here
//        return null;
//    }

//    private ISymbol? GetReturnedSymbol(IOperation? exitNode)
//    {
//        if (exitNode is IReturnOperation returnOp && returnOp.ReturnedValue != null)
//        {
//            // If return value is a simple variable, return its symbol
//            ISymbol? returnedValueSymbol = GetOperationValueSymbol(returnOp.ReturnedValue);
//            if (returnedValueSymbol != null) return returnedValueSymbol;

//            // If return value is e.g. a method call result, need a way to represent that value.
//            // Often, IFDS uses a special symbol/fact representing the return value.
//            // Returning null here for simplicity.
//        }
//        // Handle yield return, etc.
//        return null;
//    }

//    private ISymbol? GetAssignmentTargetAtReturnSite(IOperation? callSite)
//    {
//        if (callSite == null) return null;
//        // Find what variable receives the result of the callSite invocation/creation
//        if (callSite.Parent is IAssignmentOperation assignment && assignment.Value == callSite)
//        {
//            return GetOperationTargetSymbol(assignment.Target);
//        }
//        if (callSite.Parent is IVariableDeclaratorOperation declarator && declarator.Initializer?.Value == callSite)
//        {
//            return declarator.Symbol;
//        }
//        // Handle field initializers, property initializers etc.
//        return null;
//    }
//}

