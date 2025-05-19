using Microsoft.CodeAnalysis;


namespace MauiBlazorAnalyzer.Core.EntryPoints;

/// <summary>
/// Represents the type of Blazor entry point identified.
/// </summary>
public enum EntryPointType
{
    /// <summary>
    /// A property setter decorated with [Parameter] or [CascadingParameter].
    /// Taint Source: Value assigned to the property.
    /// </summary>
    ParameterSetter,

    /// <summary>
    /// A method invoked via JS Interop, decorated with [JSInvokable].
    /// Taint Source: Arguments passed to the method from JavaScript.
    /// </summary>
    JSInvokableMethod,

    /// <summary>
    /// A component lifecycle method (e.g., OnInitializedAsync).
    /// Taint Source: Potential external data fetched within the method.
    /// </summary>
    LifecycleMethod,

    /// <summary>
    /// A method handler attached to a UI event (e.g., @onclick).
    /// Taint Source: Method execution triggered by UI, potentially accessing tainted state.
    /// </summary>
    EventHandlerMethod,

    /// <summary>
    /// A lambda created by EventCallbackFactory.CreateBinder produced by an @bind directive.
    /// Taint source: the first parameter of the lambda (value from the UI).
    /// The taint is assigned to <see cref="AssociatedSymbol"/>.
    /// </summary>
    BindingCallback

}

/// <summary>
/// Base record for storing information about an identified entry point.
/// </summary>
public record EntryPointInfo
{
    /// <summary>
    /// The type of the entry point.
    /// </summary>
    public EntryPointType Type { get; init; }

    /// <summary>
    /// The fully qualified name of the component class containing the entry point.
    /// </summary>
    public string ContainingTypeName { get; init; } = string.Empty;

    /// <summary>
    /// The Symbol representing the entry point (Method or Property Symbol).
    /// Use this for further analysis with the SemanticModel.
    /// </summary>
    public ISymbol? EntryPointSymbol { get; init; }

    /// <summary>
    /// An optional associated symbol providing context.
    /// - For BindingCallbackParameter: The IPropertySymbol or IFieldSymbol being assigned to.
    /// </summary>
    public ISymbol? AssociatedSymbol { get; init; } // NEW

    /// <summary>
    /// An optional relevant operation providing context.
    /// - For BindingCallbackParameter: The ISimpleAssignmentOperation within the lambda.
    /// </summary>
    public IOperation? Operation { get; init; } // NEW

    /// <summary>
    /// The specific method symbol (for methods, setters, event handlers).
    /// </summary>
    public IMethodSymbol? MethodSymbol => EntryPointSymbol as IMethodSymbol;

    /// <summary>
    /// The specific property symbol (primarily for parameters/bindings).
    /// </summary>
    public IPropertySymbol? PropertySymbol => EntryPointSymbol as IPropertySymbol;

    public IParameterSymbol? ParameterSymbol => EntryPointSymbol as IParameterSymbol; // NEW

    /// <summary>
    /// Location of the entry point declaration in source code, if available.
    /// </summary>
    public Location? Location { get; init; }

    /// <summary>
    /// A descriptive name for the entry point (e.g., Method name, Property name).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// For JSInvokable methods, lists the parameters considered taint sources.
    /// </summary>
    public IReadOnlyList<IParameterSymbol> TaintedParameters { get; init; } = []; // Primarily for JSInvokable
    public List<ISymbol>? TaintedVariables { get; set; } // For @bind targets etc.

    public override string ToString()
    {
        string locationStr = Location?.GetMappedLineSpan().ToString() ?? "N/A";
        string entrySymbolKind = EntryPointSymbol?.Kind.ToString() ?? "N/A";
        string assocSymbolInfo = AssociatedSymbol != null ? $" -> {AssociatedSymbol.Kind} {AssociatedSymbol.Name}" : "";
        string operationInfo = Operation != null ? $" (Op: {Operation.Kind})" : "";
        string paramsInfo = TaintedParameters.Any()
            ? $" (TaintedParams: {string.Join(", ", TaintedParameters.Select(p => p.Name))})"
            : "";

        return $"{Type}: {ContainingTypeName}.{Name} ({entrySymbolKind}){assocSymbolInfo}{paramsInfo}{operationInfo} [{locationStr}]";
    }
}