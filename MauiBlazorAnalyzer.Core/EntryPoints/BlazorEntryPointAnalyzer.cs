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

    private static readonly HashSet<string> LifecycleMethodNames = new()
    {
        "OnInitialized", "OnInitializedAsync",
        "OnParametersSet", "OnParametersSetAsync",
        "OnAfterRender", "OnAfterRenderAsync",
        "SetParametersAsync",
        "ShouldRender"
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
        _eventCallbackFactorySymbol ??= compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallbackFactory"); // Used later
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
        FindJSInvokableMethods(componentTypeSymbol, entryPoints); // Can be in components too
        FindLifecycleMethods(componentTypeSymbol, entryPoints);
        FindEventHandlerMethods(componentTypeSymbol, entryPoints);
        //FindBindingSetters(componentTypeSymbol, entryPoints); // Add later if needed
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
                // Important: Check if the type itself is from the source assembly before yielding
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _sourceAssembly))
                {
                    yield return type;
                }
                // Check nested types - they belong to the containing type's assembly
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

        // Add checks for other methods like OnParametersSet, OnAfterRender...
        // SetParametersAsync is often Task SetParametersAsync(ParameterView parameters)
        if (methodSymbol.Name == "SetParametersAsync" && methodSymbol.Parameters.Length == 1 && IsTaskType(methodSymbol.ReturnType))
        {
            // Ideally check parameter type is ParameterView or related
            // For simplicity now, just check name, param count, and return type
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
                                     .FirstOrDefault(m => m.Parameters.Length == 1); // Basic check for the correct overload

        if (buildRenderTreeMethod == null) return;

        foreach (var syntaxRef in buildRenderTreeMethod.DeclaringSyntaxReferences)
        {
            var syntaxTree = syntaxRef.SyntaxTree;
            var semanticModel = _compilation.GetSemanticModel(syntaxTree);
            var walker = new EventHandlerSyntaxWalker(semanticModel, _eventCallbackFactorySymbol, componentTypeSymbol, entryPoints);
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
    private readonly INamedTypeSymbol? _eventCallbackFactorySymbol;
    private readonly INamedTypeSymbol _componentTypeSymbol;
    private readonly List<EntryPointInfo> _entryPoints;

    // Keep the HashSet for efficient lookup
    private static readonly HashSet<string> BinderMethodNames = new()
    {
        "CreateBinder",
        "CreateInferred"
        // Add other binder methods if they exist/are relevant
    };
    private const string CreateMethodName = "Create";


    public EventHandlerSyntaxWalker(SemanticModel semanticModel, INamedTypeSymbol? eventCallbackFactorySymbol, INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        _eventCallbackFactorySymbol = eventCallbackFactorySymbol; // Can be null, checked later
        _componentTypeSymbol = componentTypeSymbol ?? throw new ArgumentNullException(nameof(componentTypeSymbol));
        _entryPoints = entryPoints ?? throw new ArgumentNullException(nameof(entryPoints));
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Ensure EventCallbackFactory symbol is available
        if (_eventCallbackFactorySymbol == null)
        {
            base.VisitInvocationExpression(node);
            return;
        }

        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        // Use Symbol or CandidateSymbols if necessary (e.g., during incomplete code analysis)
        var symbol = symbolInfo.Symbol as IMethodSymbol ??
                     symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (symbol == null)
        {
            // Consider logging if symbol resolution fails often
            // Console.WriteLine($"Debug: Could not resolve symbol for invocation: {node.Expression}");
            base.VisitInvocationExpression(node);
            return;
        }

        // --- Crucial Check: Ensure the method belongs to EventCallbackFactory ---
        // Use OriginalDefinition to handle potential generic constructions correctly
        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType?.OriginalDefinition, _eventCallbackFactorySymbol))
        {
            base.VisitInvocationExpression(node);
            return;
        }

        // Use the resolved symbol's name for checks
        string methodName = symbol.Name;
        bool isBinder = BinderMethodNames.Contains(methodName);
        bool isCreate = methodName == CreateMethodName;

        // --- Logging for Debugging `isBinder` issues ---
        // Console.WriteLine($"Debug: Visiting invocation of {symbol.ContainingType?.Name}.{methodName}. isBinder={isBinder}, isCreate={isCreate}");
        // --- End Debugging ---

        if (!isBinder && !isCreate)
        {
            base.VisitInvocationExpression(node); // Ignore other methods like CreateCore
            return;
        }

        // Arguments: Receiver (this), Handler/Lambda, CurrentValue (for binder)
        if (node.ArgumentList == null || node.ArgumentList.Arguments.Count < 2)
        {
            base.VisitInvocationExpression(node);
            return;
        }

        var secondArgExpr = node.ArgumentList.Arguments[1].Expression;

        // --- Handle @bind -> CreateBinder / CreateInferred ---
        if (isBinder)
        {
            if (secondArgExpr is LambdaExpressionSyntax lambdaSyntax)
            {
                // --- Refactored Logic ---
                AnalyzeBindingLambda(lambdaSyntax);
            }
            // else: Binder used with something other than a direct lambda? Log or handle if necessary.
        }
        // --- Handle @onclick etc. -> Create ---
        else if (isCreate)
        {
            ISymbol? handlerSym = ResolveHandlerSymbol(secondArgExpr);
            if (handlerSym is IMethodSymbol eventHandlerMethod &&
                SymbolEqualityComparer.Default.Equals(eventHandlerMethod.ContainingType, _componentTypeSymbol))
            {
                // Avoid adding duplicates
                if (!_entryPoints.Any(ep => ep.Type == EntryPointType.EventHandlerMethod &&
                                            SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, eventHandlerMethod)))
                {
                    _entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.EventHandlerMethod,
                        ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                        EntryPointSymbol = eventHandlerMethod,
                        Name = eventHandlerMethod.Name,
                        Location = eventHandlerMethod.Locations.FirstOrDefault() ?? node.GetLocation()
                        // AssociatedSymbol = null, // Not needed here
                        // Operation = null, // Not needed here
                    });
                }
            }
            // else: Handler is not a method symbol or not in the component type (e.g., could be a delegate instance)
        }

        // Continue walking the tree
        base.VisitInvocationExpression(node);
    }


    private ISymbol? ResolveHandlerSymbol(ExpressionSyntax expr)
    {
        // Simplified: Get symbol info directly
        var symbolInfo = _semanticModel.GetSymbolInfo(expr);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        // Original switch was fine too, but this covers IdentifierName, MemberAccess, etc.
    }

    // --- Renamed and Refactored ---
    private void AnalyzeBindingLambda(LambdaExpressionSyntax lambdaSyntax)
    {
        IOperation? lambdaOperation = _semanticModel.GetOperation(lambdaSyntax);

        // Ensure we have an anonymous function operation with a single parameter
        if (lambdaOperation is not IAnonymousFunctionOperation { Symbol.Parameters.Length: 1 } fn)
        {
            // Console.WriteLine($"Debug: Lambda for binding is not an IAnonymousFunctionOperation with 1 parameter: {lambdaSyntax}");
            return;
        }

        IParameterSymbol lambdaParameterSymbol = fn.Symbol.Parameters[0]; // The Taint Source

        // Find the assignment(s) within the lambda body that use the parameter
        foreach (var assign in fn.Body.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
        {
            // Check if the value being assigned comes *from* the lambda parameter
            // This check ensures we only care about assignments like 'Target = lambdaParam;'
            // or potentially 'Target = Process(lambdaParam);' if Process returns the taint.
            // For simplicity now, we focus on direct assignment: 'Target = lambdaParam;'
            if (assign.Value is IParameterReferenceOperation paramRef &&
                SymbolEqualityComparer.Default.Equals(paramRef.Parameter, lambdaParameterSymbol))
            {
                // Resolve the target of the assignment (LHS) - Must be a field/property of the component
                ISymbol? targetSymbol = assign.Target switch
                {
                    // Check instance is 'this' implicitly or explicitly
                    IPropertyReferenceOperation pRef when pRef.Instance == null || pRef.Instance is IInstanceReferenceOperation => pRef.Property,
                    IFieldReferenceOperation fRef when fRef.Instance == null || fRef.Instance is IInstanceReferenceOperation => fRef.Field,
                    _ => null
                };


                // Ensure the target is a member of the component we are analyzing
                if (targetSymbol != null && SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, _componentTypeSymbol))
                {
                    // Avoid duplicates based on the assignment operation's location
                    if (_entryPoints.Any(ep => ep.Type == EntryPointType.BindingCallbackParameter &&
                                                ep.Operation?.Syntax?.Equals(assign.Syntax) == true))
                    {
                        continue;
                    }

                    _entryPoints.Add(new EntryPointInfo
                    {
                        Type = EntryPointType.BindingCallbackParameter,
                        ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                        // --- Key Changes ---
                        EntryPointSymbol = lambdaParameterSymbol,   // The Parameter is the source entry point
                        AssociatedSymbol = targetSymbol,           // The Field/Property being assigned to
                        Operation = assign,                        // The assignment operation itself
                        // --- End Key Changes ---
                        Name = $"{targetSymbol.Name} (bind source: {lambdaParameterSymbol.Name})", // Descriptive name
                        Location = assign.Syntax.GetLocation() // Location of the assignment
                    });

                    // Typically, a @bind lambda has only one direct assignment using the parameter.
                    // If more complex scenarios exist, you might only want the first one.
                    // break; // Uncomment if only the first assignment is desired.
                }
                // else: Assignment target is not a component field/property or couldn't be resolved.
            }
            // else: Assignment doesn't directly use the lambda parameter on the RHS. Ignore for now.
        }
    }
}
