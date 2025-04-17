using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MauiBlazorAnalyzer.Core.Analysis;
public static class MauiEntryPointLocator
{
    public static IEnumerable<IMethodSymbol> FindEntryPoints(Compilation compilation)
    {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));

        bool isConcrete(INamedTypeSymbol? type, IMethodSymbol m) =>
            !(m.IsAbstract || m.IsExtern) && m.DeclaringSyntaxReferences.Length > 0 &&
            SymbolEqualityComparer.Default.Equals(type?.ContainingAssembly, compilation.Assembly);

        // 1. MAUI bootstrap
        var mauiProgram = compilation.GetTypeByMetadataName("MauiProgram");
        if (mauiProgram != null)
        {
            foreach (var m in mauiProgram.GetMembers("CreateMauiApp").OfType<IMethodSymbol>())
                if (isConcrete(mauiProgram, m))
                    yield return m;
        }

        // 2. Blazor Components
        var componentBase = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ComponentBase");
        if (componentBase != null)
        {
            foreach (var component in GetAllTypes(compilation.GlobalNamespace))
            {
                if (!InheritsFrom(component, componentBase)) continue;

                foreach (var m in component.GetMembers().OfType<IMethodSymbol>())
                {
                    if (m.Name is "OnInitialized" or "OnInitializedAsync" or "OnParametersSet" or "OnParametersSetAsync")
                        if (isConcrete(component, m))
                            yield return m;
                }
            }
        }

        // 3. Maui pages
        var contentPage = compilation.GetTypeByMetadataName("Microsoft.Maui.Controls.ContentPage");
        if (contentPage != null)
        {
            foreach (var page in GetAllTypes(compilation.GlobalNamespace))
            {
                if (!InheritsFrom(page, contentPage)) continue;

                foreach (var m in page.GetMembers().OfType<IMethodSymbol>())
                {
                    if (m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public)
                        if (isConcrete(page, m))
                            yield return m;
                }
            }
        }

        // 4. JS-Interop callbacks
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var methodDeclaration in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                if (symbol == null) continue;
                if (symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Microsoft.JSInterop.JSInvokableAttribute"))
                    if (isConcrete(symbol.ContainingType, symbol))
                        yield return symbol;
            }
        }

    }
    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
            yield return t;
        foreach (var inner in ns.GetNamespaceMembers())
            foreach (var t in GetAllTypes(inner))
                yield return t;
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
    {
        while (type != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, baseType))
                return true;
            type = type.BaseType;
        }
        return false;
    }
}
