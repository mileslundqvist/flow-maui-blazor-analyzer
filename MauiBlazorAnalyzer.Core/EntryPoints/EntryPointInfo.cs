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
    /// A property setter invoked implicitly via data binding (@bind).
    /// Taint Source: Value assigned to the property from the UI element.
    /// </summary>
    BindingSetter // Note: Harder to detect reliably without deeper analysis
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
    /// The specific method symbol (for methods, setters, event handlers).
    /// </summary>
    public IMethodSymbol? MethodSymbol => EntryPointSymbol as IMethodSymbol;

    /// <summary>
    /// The specific property symbol (primarily for parameters/bindings).
    /// </summary>
    public IPropertySymbol? PropertySymbol => EntryPointSymbol as IPropertySymbol;


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

    public override string ToString()
    {
        string locationStr = Location?.GetMappedLineSpan().ToString() ?? "N/A";
        string parameters = TaintedParameters.Any()
            ? $" (Params: {string.Join(", ", TaintedParameters.Select(p => p.Name))})"
            : "";
        return $"{Type}: {ContainingTypeName}.{Name}{parameters} [{locationStr}]";
    }
}