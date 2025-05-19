using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using System.Xml.Linq;

namespace MauiBlazorAnalyzer.Core.EntryPoints;
public class BlazorEntryPointAnalyzer
{
    private readonly Compilation _compilation;
    private readonly IAssemblySymbol _sourceAssembly;
    private readonly INamedTypeSymbol? _componentBaseSymbol;
    private readonly INamedTypeSymbol? _parameterAttributeSymbol;
    private readonly INamedTypeSymbol? _cascadingParameterAttributeSymbol;
    private readonly INamedTypeSymbol? _jsInvokableAttributeSymbol;
    private readonly INamedTypeSymbol? _eventCallbackFactorySymbol;

    private readonly INamedTypeSymbol? _renderTreeBuilderSymbol; // Add symbol for RenderTreeBuilder


    private static readonly HashSet<string> LifecycleMethodNames = new()
    {
        "OnInitialized", "OnInitializedAsync",
        "OnParametersSet", "OnParametersSetAsync",
        "OnAfterRender", "OnAfterRenderAsync",
        "SetParametersAsync",
        "ShouldRender"
    };

    // Helper set for typical @bind event attribute names
    private static readonly HashSet<string> BindEventAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "onchange",
        "oninput"
        // Add other events if needed (e.g., for custom elements/bindings)
    };

    public BlazorEntryPointAnalyzer(Compilation compilation)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _sourceAssembly = compilation.Assembly;

        // Symbols for some well-known types needed for analysis
        _componentBaseSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ComponentBase");
        _parameterAttributeSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ParameterAttribute");
        _cascadingParameterAttributeSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.CascadingParameterAttribute");
        _jsInvokableAttributeSymbol = compilation.GetTypeByMetadataName("Microsoft.JSInterop.JSInvokableAttribute");
        _eventCallbackFactorySymbol ??= compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallbackFactory");
        _renderTreeBuilderSymbol = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder");
    }

    /// <summary>
    /// Analyzes the compilation and returns a list of identified Blazor entry points.
    /// </summary>
    /// <returns>A list of EntryPointInfo objects.</returns>
    public List<EntryPointInfo> FindEntryPoints()
    {
        var entryPoints = new List<EntryPointInfo>();

        if (_componentBaseSymbol == null)
        {
            Console.Error.WriteLine("Warning: Microsoft.AspNetCore.Components.ComponentBase not found in compilation references.");
            return entryPoints;
        }

        // Iterate through all named types in the compilation
        foreach (var typeSymbol in GetAllNamedTypesInAssembly(_sourceAssembly.GlobalNamespace))
        {
            if (IsDefinedInSourceAssembly(typeSymbol))
            {
                // Is it a component?
                if (_componentBaseSymbol != null && typeSymbol.TypeKind == TypeKind.Class && DerivesFrom(typeSymbol, _componentBaseSymbol))
                {
                    AnalyzeComponentType(typeSymbol, entryPoints);
                }

                // Is it just a regular class/struct that might have JSInvokable methods?
                else if ((typeSymbol.TypeKind == TypeKind.Class || typeSymbol.TypeKind == TypeKind.Struct))
                {
                    // Check for JSInvokable methods only within source assembly types
                    FindJSInvokableMethods(typeSymbol, entryPoints);
                }
            }
        }

        return entryPoints;
    }

    /// <summary>
    /// Checks if the type symbol is defined within the source assembly being analyzed.
    /// </summary>
    private bool IsDefinedInSourceAssembly(INamedTypeSymbol typeSymbol)
    {
        // Check if the containing assembly is the same as the compilation's assembly
        return SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, _sourceAssembly);
    }

    /// <summary>
    /// Analyzes a specific component type symbol for various entry points.
    /// </summary>
    private void AnalyzeComponentType(INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        FindParameterSetters(componentTypeSymbol, entryPoints);
        FindJSInvokableMethods(componentTypeSymbol, entryPoints);
        FindLifecycleMethods(componentTypeSymbol, entryPoints);
        FindEventHandlerMethods(componentTypeSymbol, entryPoints);
        FindBindingCallbacks(componentTypeSymbol, entryPoints);
    }

    private void FindBindingCallbacks(
        INamedTypeSymbol componentType,
        List<EntryPointInfo> entryPoints)
    {
        // Find the generated BuildRenderTree method
        var buildRenderTreeMethod = componentType.GetMembers("BuildRenderTree")
                                             .OfType<IMethodSymbol>()
                                             .FirstOrDefault(m => !m.IsStatic &&
                                                                 m.Parameters.Length == 1 &&
                                                                 SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type.OriginalDefinition, _renderTreeBuilderSymbol)); // Use symbol comparison

        if (buildRenderTreeMethod == null || _eventCallbackFactorySymbol == null || _renderTreeBuilderSymbol == null)
        {
            // Required symbols or method not found
            return;
        }

        foreach (var syntaxRef in buildRenderTreeMethod.DeclaringSyntaxReferences)
        {
            var model = _compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var rootNode = syntaxRef.GetSyntax();
            var operation = model.GetOperation(rootNode);
            if (operation == null) continue;

            // Find all invocations within the BuildRenderTree method body
            foreach (var invocationOp in operation.Descendants().OfType<IInvocationOperation>())
            {
                // Check if it's RenderTreeBuilder.AddAttribute(...)
                if (invocationOp.TargetMethod.Name == "AddAttribute" &&
                    SymbolEqualityComparer.Default.Equals(invocationOp.TargetMethod.ContainingType?.OriginalDefinition, _renderTreeBuilderSymbol) &&
                    invocationOp.Arguments.Length >= 3) // Need at least sequence, name, value
                {
                    // Argument 1: Attribute Name (string literal)
                    var nameArg = invocationOp.Arguments.FirstOrDefault(a => a.Parameter?.Name == "name");
                    if (nameArg == null && invocationOp.Arguments.Length > 1) nameArg = invocationOp.Arguments[1]; // Fallback to index

                    if (nameArg?.Value is ILiteralOperation { ConstantValue: { HasValue: true, Value: string attrName } } &&
                        BindEventAttributeNames.Contains(attrName)) // Is it a known binding event like "onchange"?
                    {

                        // Argument 2: Attribute Value (expecting EventCallbackFactory.Create)
                        var valueArg = invocationOp.Arguments.FirstOrDefault(a => a.Parameter?.Name == "value");
                        if (valueArg == null && invocationOp.Arguments.Length > 2) valueArg = invocationOp.Arguments[2]; // Fallback to index


                        if (valueArg?.Value is IInvocationOperation valueInvocation )
                        {
                            IDelegateCreationOperation? delegateCreation = null;

                            // Check 1: Is the method name "Create"? (Using exact match ==)
                            bool isCreateMethod = valueInvocation.TargetMethod.Name == "Create";

                            // Check 2: Does "Create" have enough arguments? (>= 2)
                            bool createHasEnoughArguments = valueInvocation.Arguments.Length >= 2;

                            if (isCreateMethod && createHasEnoughArguments)
                            {
                                var handlerDelegateArg = valueInvocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "callback") ?? (valueInvocation.Arguments.Length > 1 ? valueInvocation.Arguments[1] : null);
                                delegateCreation = handlerDelegateArg?.Value as IDelegateCreationOperation;
                            }
                            else
                            {

                                // Check 3: Is the method name "CreateBinder"?
                                bool isCreateBinderMethod = valueInvocation.TargetMethod.Name == "CreateBinder";

                                // Check 4: Does "CreateBinder" have enough arguments? (>= 3)
                                bool binderHasEnoughArguments = valueInvocation.Arguments.Length >= 3;

                                // Combine the checks for the "CreateBinder" case
                                if (isCreateBinderMethod && binderHasEnoughArguments)
                                {
                                    // Extracted logic for "CreateBinder" case:
                                    var setterArgument = valueInvocation.Arguments.FirstOrDefault(arg => arg.Parameter?.Name == "setter") ?? (valueInvocation.Arguments.Length > 1 ? valueInvocation.Arguments[1] : null);
                                    delegateCreation = setterArgument?.Value as IDelegateCreationOperation;
                                }
                            }
                            if (delegateCreation != null)
                            {
                                // Pass attrName to the helper method
                                AnalyzeBindingSetterDelegate(delegateCreation, componentType, entryPoints, valueInvocation.Syntax.GetLocation(), attrName);
                            }
                        }
                    }
                }

                // This might be used for component parameter binding (@bind-Value) or programmatically created bindings.
                else if (invocationOp.TargetMethod.Name == "CreateBinder" &&
                         SymbolEqualityComparer.Default.Equals(invocationOp.TargetMethod.ContainingType?.OriginalDefinition, _eventCallbackFactorySymbol) &&
                         invocationOp.Arguments.Length >= 3) // Need receiver, setter, getter
                {
                    var setterArgument = invocationOp.Arguments.FirstOrDefault(arg => arg.Parameter?.Name == "setter"); // Find by name "setter"
                    if (setterArgument == null && invocationOp.Arguments.Length > 1) setterArgument = invocationOp.Arguments[1]; // Fallback to index 1

                    if (setterArgument?.Value is IDelegateCreationOperation delegateCreation)
                    {
                        AnalyzeBindingSetterDelegate(delegateCreation, componentType, entryPoints, invocationOp.Syntax.GetLocation(), null);
                    }
                }
            }
        }
    }


    private void AnalyzeBindingSetterDelegate(
        IDelegateCreationOperation delegateCreation,
        INamedTypeSymbol componentType,
        List<EntryPointInfo> entryPoints,
        Location diagnosticLocation,
        string? attrName
        )
    {
        if (delegateCreation.Target is IAnonymousFunctionOperation lambdaOp)
        {
            // Find the assignment operation within the lambda body
            ISimpleAssignmentOperation? assignment = FindAssignmentInLambda(lambdaOp);

            if (assignment != null)
            {
                // Analyze the target of the assignment (LHS)
                ISymbol? targetSymbol = GetAssignmentTargetSymbol(assignment, componentType);

                if (targetSymbol != null)
                {
                    // Avoid adding duplicates based on the target symbol AND the lambda generating the assignment
                    if (!entryPoints.Any(ep => ep.Type == EntryPointType.BindingCallback &&
                                               SymbolEqualityComparer.Default.Equals(ep.AssociatedSymbol, targetSymbol) &&
                                               SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, lambdaOp.Symbol)))
                    {
                        entryPoints.Add(new EntryPointInfo
                        {
                            Type = EntryPointType.BindingCallback,
                            ContainingTypeName = componentType.ToDisplayString(),
                            EntryPointSymbol = lambdaOp.Symbol, // The synthetic method for the lambda
                            AssociatedSymbol = targetSymbol,   // The Property/Field being assigned to (this is what gets tainted)
                            Operation = assignment,          // Store the assignment operation for context
                            Name = $"@bind-update:{attrName ?? "event"} → {targetSymbol.Name}", // Include event name if possible
                            Location = diagnosticLocation, // Use location of the Create call or AddAttribute call
                            TaintedVariables = new List<ISymbol> { targetSymbol } // Explicitly list the tainted variable
                        });
                    }
                }
            }
        }
        else if (delegateCreation.Target is IMethodReferenceOperation methodRef)
        {
            var targetMethod = methodRef.Method;
            // Ensure the method belongs to the component and is suitable (e.g., takes one parameter)
            if (SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, componentType) &&
                targetMethod.Parameters.Length > 0) // Assume first parameter is the value source
            {
                // The *method itself* is the entry point, and its *parameters* are the source of taint.
                // However, for IFDS, we are interested in where the taint *goes*.
                // We need to analyze the *body* of 'targetMethod' to see which field/property it assigns to.
                // This requires getting the IOperation for targetMethod's body, which is more complex.

                // Simpler approach for now: Add the method as an entry point, maybe mark parameters as tainted.
                // The IFDS analysis itself would then track taint flow from the parameter *inside* the method.
                if (!entryPoints.Any(ep => ep.Type == EntryPointType.BindingCallback &&
                                           SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, targetMethod)))
                {
                    entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.BindingCallback,
                        ContainingTypeName = componentType.ToDisplayString(),
                        EntryPointSymbol = targetMethod, // The actual method used as callback
                        AssociatedSymbol = null, // Target isn't known without analyzing the method body
                        Operation = methodRef,
                        Name = $"@bind-method:{attrName ?? "event"} → {targetMethod.Name}",
                        Location = diagnosticLocation,
                        TaintedParameters = targetMethod.Parameters.ToList() // Mark parameters as potential sources
                    });
                }
            }
        }
    }

    // Helper to find the assignment within a lambda (extracted for clarity)
    private ISimpleAssignmentOperation? FindAssignmentInLambda(IAnonymousFunctionOperation lambdaOp)
    {
        if (lambdaOp.Body is IBlockOperation blockBody)
        {
            // Look for the first simple assignment statement in the block
            return blockBody.Operations.OfType<IExpressionStatementOperation>()
                                       .Select(stmt => stmt.Operation)
                                       .OfType<ISimpleAssignmentOperation>()
                                       .FirstOrDefault();
        }
        return null; // Not found or unsupported structure
    }

    // Helper to get the Field/Property symbol from the assignment target (extracted for clarity)
    private ISymbol? GetAssignmentTargetSymbol(ISimpleAssignmentOperation assignment, INamedTypeSymbol componentType)
    {
        IOperation targetOperation = assignment.Target;
        // Unwrap potential conversions
        while (targetOperation is IConversionOperation conversion)
        {
            targetOperation = conversion.Operand;
        }

        ISymbol? targetSymbol = null;
        switch (targetOperation)
        {
            case IPropertyReferenceOperation propRef:
                targetSymbol = propRef.Property;
                break;
            case IFieldReferenceOperation fieldRef:
                targetSymbol = fieldRef.Field;
                break;
        }

        // Validate: Ensure the symbol exists, is assignable (Property needs setter), and belongs to the component
        if (targetSymbol != null &&
            (targetSymbol is IFieldSymbol || targetSymbol is IPropertySymbol p && p.SetMethod != null) &&
            SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, componentType))
        {
            return targetSymbol;
        }

        return null; // Invalid or external target
    }

    /// <summary>
    /// Recursively finds all named type symbols within a namespace.
    /// </summary>
    private IEnumerable<INamedTypeSymbol> GetAllNamedTypesInAssembly(INamespaceSymbol globalNamespace)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(globalNamespace);

        while (stack.Count > 0)
        {
            var currentSymbol = stack.Pop();

            if (currentSymbol is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    stack.Push(member);
                }
            }
            else if (currentSymbol is INamedTypeSymbol type)
            {
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _sourceAssembly))
                {
                    yield return type;
                }

                foreach (var nestedType in type.GetTypeMembers())
                {
                    // No need for assembly check here, nested types are part of the containing type's assembly
                    stack.Push(nestedType);
                }
            }
        }
    }

    /// <summary>
    /// Checks if a type symbol derives from a specific base type symbol.
    /// </summary>
    private bool DerivesFrom(ITypeSymbol typeSymbol, INamedTypeSymbol baseTypeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseTypeSymbol))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Finds property setters marked with [Parameter] or [CascadingParameter].
    /// </summary>
    private void FindParameterSetters(INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        if (_parameterAttributeSymbol == null && _cascadingParameterAttributeSymbol == null) return;

        foreach (var member in componentTypeSymbol.GetMembers())
        {
            if (member is IPropertySymbol propertySymbol && propertySymbol.SetMethod != null)
            {
                bool isParameter = propertySymbol.GetAttributes().Any(attr =>
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _parameterAttributeSymbol) ||
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _cascadingParameterAttributeSymbol));

                if (isParameter)
                {
                    entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.ParameterSetter,
                        ContainingTypeName = componentTypeSymbol.ToDisplayString(),
                        EntryPointSymbol = propertySymbol.SetMethod,
                        Name = $"{propertySymbol.Name} (set)",
                        Location = propertySymbol.Locations.FirstOrDefault()
                    });
                }
            }
        }
    }

    /// <summary>
    /// Finds methods marked with [JSInvokable].
    /// </summary>
    private void FindJSInvokableMethods(INamedTypeSymbol typeSymbol, List<EntryPointInfo> entryPoints)
    {
        if (_jsInvokableAttributeSymbol == null) return;

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary) // Exclude constructors, operators etc.
            {
                bool isJSInvokable = methodSymbol.GetAttributes().Any(attr =>
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _jsInvokableAttributeSymbol));

                if (isJSInvokable)
                {
                    entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.JSInvokableMethod,
                        ContainingTypeName = typeSymbol.ToDisplayString(),
                        EntryPointSymbol = methodSymbol,
                        Name = methodSymbol.Name,
                        Location = methodSymbol.Locations.FirstOrDefault(),
                        TaintedParameters = methodSymbol.Parameters.ToList() // All parameters are potential sources
                    });
                }
            }
        }
    }

    /// <summary>
    /// Finds overrides of standard Blazor lifecycle methods.
    /// </summary>
    private void FindLifecycleMethods(INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        foreach (var member in componentTypeSymbol.GetMembers())
        {
            // Check if it's a method, is an override, and its name is in our known set
            if (member is IMethodSymbol methodSymbol &&
                (methodSymbol.IsOverride || IsPotentialLifecycleMethod(methodSymbol)) && // Include non-overrides if directly implemented
                LifecycleMethodNames.Contains(methodSymbol.Name))
            {
                // Ensure we don't add duplicates if a method overrides *and* is potentially lifecycle
                if (!entryPoints.Any(ep => SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, methodSymbol) && ep.Type == EntryPointType.LifecycleMethod))
                {
                    entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.LifecycleMethod,
                        ContainingTypeName = componentTypeSymbol.ToDisplayString(),
                        EntryPointSymbol = methodSymbol,
                        Name = methodSymbol.Name,
                        Location = methodSymbol.Locations.FirstOrDefault()
                    });
                }
            }
        }
    }

    /// <summary>
    /// Helper to check if a method signature potentially matches a lifecycle method
    /// (e.g., if implementing an interface directly, not overriding).
    /// This is a basic check; more robust checking might be needed.
    /// </summary>
    private bool IsPotentialLifecycleMethod(IMethodSymbol methodSymbol)
    {
        // Example check: OnInitialized might be public void OnInitialized()
        if (methodSymbol.Name == "OnInitialized" && methodSymbol.Parameters.IsEmpty && methodSymbol.DeclaredAccessibility == Accessibility.Protected && methodSymbol.ReturnType.SpecialType == SpecialType.System_Void) return true;
        // Example check: OnInitializedAsync might be protected virtual Task OnInitializedAsync()
        if (methodSymbol.Name == "OnInitializedAsync" && methodSymbol.Parameters.IsEmpty && methodSymbol.DeclaredAccessibility == Accessibility.Protected && IsTaskType(methodSymbol.ReturnType)) return true;

        if (methodSymbol.Name == "SetParametersAsync" && methodSymbol.Parameters.Length == 1 && IsTaskType(methodSymbol.ReturnType))
        {
            return true;
        }


        return false; // Default case
    }

    private bool IsTaskType(ITypeSymbol typeSymbol)
    {
        return typeSymbol.Name == "Task" && typeSymbol.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
    }


    /// <summary>
    /// Finds methods potentially used as event handlers (@onclick, etc.).
    /// This implementation uses a SyntaxWalker to analyze BuildRenderTree.
    /// </summary>
    private void FindEventHandlerMethods(INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        var buildRenderTreeMethod = componentTypeSymbol.GetMembers("BuildRenderTree")
                                     .OfType<IMethodSymbol>()
                                     .FirstOrDefault(m => m.Parameters.Length == 1);

        if (buildRenderTreeMethod == null) return;

        foreach (var syntaxRef in buildRenderTreeMethod.DeclaringSyntaxReferences)
        {
            var syntaxTree = syntaxRef.SyntaxTree;
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            var walker = new EventHandlerSyntaxWalker(semanticModel, _renderTreeBuilderSymbol, _eventCallbackFactorySymbol, componentTypeSymbol, entryPoints);
            walker.Visit(syntaxRef.GetSyntax()); // Walk the syntax node of the BuildRenderTree method
        }
    }
}

/// <summary>
/// Syntax Walker specifically designed to find event handler registrations
/// within a BuildRenderTree method.
/// </summary>
internal class EventHandlerSyntaxWalker : CSharpSyntaxWalker
{
    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _renderTreeBuilderSymbol;
    private readonly INamedTypeSymbol? _eventCallbackFactorySymbol;
    private readonly INamedTypeSymbol _componentTypeSymbol;
    private readonly List<EntryPointInfo> _entryPoints;

    private static readonly HashSet<string> EventAttributePrefixes = new(StringComparer.OrdinalIgnoreCase) { "on" };


    public EventHandlerSyntaxWalker(SemanticModel semanticModel,
        INamedTypeSymbol? renderTreeBuilderSymbol,
        INamedTypeSymbol? eventCallbackFactorySymbol, 
        INamedTypeSymbol componentTypeSymbol,
        List<EntryPointInfo> entryPoints)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        _renderTreeBuilderSymbol = renderTreeBuilderSymbol;
        _eventCallbackFactorySymbol = eventCallbackFactorySymbol; // Can be null, checked later
        _componentTypeSymbol = componentTypeSymbol ?? throw new ArgumentNullException(nameof(componentTypeSymbol));
        _entryPoints = entryPoints ?? throw new ArgumentNullException(nameof(entryPoints));
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Ensure EventCallbackFactory symbol is available
        if (_renderTreeBuilderSymbol == null || _eventCallbackFactorySymbol == null)
        {
            base.VisitInvocationExpression(node);
            return;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        var invokedMethodSymbol = symbolInfo.Symbol as IMethodSymbol ??
                                 symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (invokedMethodSymbol == null ||
            invokedMethodSymbol.Name != "AddAttribute" ||
            !SymbolEqualityComparer.Default.Equals(invokedMethodSymbol.ContainingType?.OriginalDefinition, _renderTreeBuilderSymbol) ||
            node.ArgumentList == null ||
            node.ArgumentList.Arguments.Count < 3) // Need sequence, name, value
        {
            base.VisitInvocationExpression(node); // Continue walking children
            return;
        }


        // --- Check if the attribute name looks like an event ---
        // Argument 1 (index 1) should be the attribute name
        var nameArgExpr = node.ArgumentList.Arguments.Count > 1 ? node.ArgumentList.Arguments[1].Expression : null;
        Optional<object?> nameConstValue = _semanticModel.GetConstantValue(nameArgExpr);
        if (!nameConstValue.HasValue || nameConstValue.Value is not string attrName ||
            !EventAttributePrefixes.Any(prefix => attrName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }


        // --- Check if the attribute value (Argument 2, index 2) is EventCallbackFactory.Create ---
        var valueArgExpr = node.ArgumentList.Arguments.Count > 2 ? node.ArgumentList.Arguments[2].Expression : null;
        if (valueArgExpr is InvocationExpressionSyntax valueInvocationExpr) // Is the value an invocation?
        {
            var valueSymbolInfo = _semanticModel.GetSymbolInfo(valueInvocationExpr);
            var valueMethodSymbol = valueSymbolInfo.Symbol as IMethodSymbol ??
                                    valueSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

            // Is it EventCallbackFactory.Create?
            if (valueMethodSymbol != null &&
                valueMethodSymbol.Name == "Create" && // Use constant if defined
                SymbolEqualityComparer.Default.Equals(valueMethodSymbol.ContainingType?.OriginalDefinition, _eventCallbackFactorySymbol) &&
                valueInvocationExpr.ArgumentList != null &&
                valueInvocationExpr.ArgumentList.Arguments.Count >= 2) // Needs receiver and callback
            {
                // --- Found the pattern! Extract the handler method ---
                var handlerArgExpr = valueInvocationExpr.ArgumentList.Arguments[1].Expression;
                ISymbol? handlerSymbol = ResolveHandlerSymbol(handlerArgExpr);

                if (handlerSymbol is IMethodSymbol eventHandlerMethod &&
                    SymbolEqualityComparer.Default.Equals(eventHandlerMethod.ContainingType, _componentTypeSymbol))
                {
                    // Add entry point if not already added
                    if (!_entryPoints.Any(ep => ep.Type == EntryPointType.EventHandlerMethod &&
                                                 SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, eventHandlerMethod)))
                    {
                        _entryPoints.Add(new EntryPointInfo
                        {
                            Type = EntryPointType.EventHandlerMethod,
                            ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                            EntryPointSymbol = eventHandlerMethod,
                            Name = $"{attrName} → {eventHandlerMethod.Name}", // Include event name
                            Location = eventHandlerMethod.Locations.FirstOrDefault() ?? node.GetLocation(), // Prefer method location
                        });
                    }
                    // We found the handler, no need to visit children of this AddAttribute call further
                    return;
                }
            }
        }
        base.VisitInvocationExpression(node);
    }

    private ISymbol? ResolveHandlerSymbol(ExpressionSyntax expr)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(expr);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }
}
