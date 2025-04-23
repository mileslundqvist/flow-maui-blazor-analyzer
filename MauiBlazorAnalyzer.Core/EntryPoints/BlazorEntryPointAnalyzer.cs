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
        // FindBindingSetters(componentTypeSymbol, entryPoints); // Add later if needed
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

    public EventHandlerSyntaxWalker(SemanticModel semanticModel, INamedTypeSymbol? eventCallbackFactorySymbol, INamedTypeSymbol componentTypeSymbol, List<EntryPointInfo> entryPoints)
    {
        _semanticModel = semanticModel;
        _eventCallbackFactorySymbol = eventCallbackFactorySymbol;
        _componentTypeSymbol = componentTypeSymbol;
        _entryPoints = entryPoints;
    }

    private static readonly string[] _binderNames = ["CreateBinder", "CreateInferred"];
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        if (symbolInfo.Symbol is IMethodSymbol invokedMethodSymbol &&
            SymbolEqualityComparer.Default.Equals(invokedMethodSymbol.ContainingType, _eventCallbackFactorySymbol) &&
            invokedMethodSymbol.Name == "Create")
        {

            if (invokedMethodSymbol.ContainingType.Equals(_eventCallbackFactorySymbol, SymbolEqualityComparer.Default) &&
                _binderNames.Contains(invokedMethodSymbol.Name))
            {
                // Safety: needs ≥ 2 args   receiver, lambda, …
                if (node.ArgumentList.Arguments.Count > 1)
                {
                    var assignmentLambda = node.ArgumentList.Arguments[1].Expression as LambdaExpressionSyntax;
                    if (assignmentLambda != null)
                    {
                        AnalyseAssignmentLambda(assignmentLambda);
                    }
                }
            }

            if (node.ArgumentList.Arguments.Count > 1)
            {
                var handlerArgument = node.ArgumentList.Arguments[1].Expression; // Assuming the handler is the second arg

                ISymbol? handlerSymbol = null;

                // Case 1: Direct method group reference (e.g., Create(this, MyClickHandler))
                if (handlerArgument is IdentifierNameSyntax identifierName)
                {
                    var handlerSymbolInfo = _semanticModel.GetSymbolInfo(identifierName);
                    handlerSymbol = handlerSymbolInfo.Symbol; // Could be IMethodSymbol if directly referenced
                                                              // If it's ambiguous (method group), we might need more work or just take the first candidate
                    if (handlerSymbol == null && handlerSymbolInfo.CandidateSymbols.Any())
                    {
                        handlerSymbol = handlerSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                    }
                }
                // Case 2: Lambda expression (e.g., Create(this, async () => await MyClickHandlerAsync()))
                // Case 3: Delegate creation (e.g., Create(this, new Action<EventArgs>(MyClickHandler)))
                // TODO: Add analysis for lambda expressions and delegate creation syntax if needed.
                // This involves analyzing the body of the lambda or the method passed to the delegate constructor.

                if (handlerSymbol is IMethodSymbol eventHandlerMethodSymbol)
                {
                    // Check if the found method belongs to the component we are analyzing
                    if (SymbolEqualityComparer.Default.Equals(eventHandlerMethodSymbol.ContainingType, _componentTypeSymbol))
                    {
                        // Avoid adding duplicates
                        if (!_entryPoints.Any(ep => SymbolEqualityComparer.Default.Equals(ep.EntryPointSymbol, eventHandlerMethodSymbol) && ep.Type == EntryPointType.EventHandlerMethod))
                        {
                            _entryPoints.Add(new EntryPointInfo
                            {
                                Type = EntryPointType.EventHandlerMethod,
                                ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                                EntryPointSymbol = eventHandlerMethodSymbol,
                                Name = eventHandlerMethodSymbol.Name,
                                Location = eventHandlerMethodSymbol.Locations.FirstOrDefault() ?? node.GetLocation() // Fallback location
                            });
                        }
                    }
                }
            }
        }

        // Continue walking down the tree
        base.VisitInvocationExpression(node);
    }

    private void AnalyseAssignmentLambda(LambdaExpressionSyntax lambda)
    {
        // NB: Use 'IOperation' instead of raw syntax—far easier to inspect assignments.
        var operation = _semanticModel.GetOperation(lambda) as IAnonymousFunctionOperation;
        if (operation == null) return;

        // Look for the simple pattern “lhs = rhs”
        var assigns = operation.Body.Descendants()
                        .OfType<ISimpleAssignmentOperation>();

        foreach (var a in assigns)
        {
            // If the LHS ultimately refers to a property or field of *this* component,
            // create a BindingSetter entry.
            if (a.Target is IPropertyReferenceOperation propRef &&
                SymbolEqualityComparer.Default.Equals(propRef.Instance?.Type, _componentTypeSymbol))
            {
                var prop = propRef.Property;
                _entryPoints.Add(new EntryPointInfo
                {
                    Type = EntryPointType.BindingSetter,
                    ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                    EntryPointSymbol = prop.SetMethod,        // or the property symbol itself
                    Name = $"{prop.Name} (bind)",
                    Location = prop.Locations.FirstOrDefault()
                });
            }
            else if (a.Target is IFieldReferenceOperation fieldRef &&
                     SymbolEqualityComparer.Default.Equals(fieldRef.Instance?.Type, _componentTypeSymbol))
            {
                var field = fieldRef.Field;
                _entryPoints.Add(new EntryPointInfo
                {
                    Type = EntryPointType.BindingSetter,
                    ContainingTypeName = _componentTypeSymbol.ToDisplayString(),
                    EntryPointSymbol = field,                 // fields have no setter
                    Name = $"{field.Name} (bind)",
                    Location = field.Locations.FirstOrDefault()
                });
            }
        }
    }
}
