using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core.Analysis;
public class EntryPointProvider
{
    private const string MauiApplicationTypeName = "Microsoft.Maui.Controls.Application";
    private const string MauiPageTypeName = "Microsoft.Maui.Controls.Page";
    private const string BlazorComponentBaseTypeName = "Microsoft.AspNetCore.Components.ComponentBase";

    INamedTypeSymbol? mauiApplicationBaseType;
    INamedTypeSymbol? mauiPageBaseType;
    INamedTypeSymbol? blazorComponentBaseType;


    public HashSet<IMethodSymbol> FindEntryPoints(Compilation compilation)
    {
        mauiApplicationBaseType = compilation.GetTypeByMetadataName(MauiApplicationTypeName);
        mauiPageBaseType = compilation.GetTypeByMetadataName(MauiPageTypeName);
        blazorComponentBaseType = compilation.GetTypeByMetadataName(BlazorComponentBaseTypeName);

        var entryPoints = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        AddMainMethod(compilation, entryPoints);

        Queue<INamespaceSymbol> namespaces = new();

        namespaces.Enqueue(compilation.GlobalNamespace);

        while (namespaces.Count > 0)
        {
            INamespaceSymbol currentNamespace = namespaces.Dequeue();

            foreach (var namespaceMember in currentNamespace.GetNamespaceMembers())
            {
                namespaces.Enqueue(namespaceMember);
            }

            foreach (var typeSymbol in currentNamespace.GetTypeMembers())
            {
                ProcessTypeSymbol(typeSymbol, entryPoints);
            }
        }

        return entryPoints;
    }

    private void ProcessTypeSymbol(INamedTypeSymbol typeSymbol, HashSet<IMethodSymbol> entryPoints)
    {
        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            ProcessTypeSymbol(nestedType, entryPoints);
        }

        bool addedConstructor = false;

        if (InheritsFrom(typeSymbol, mauiApplicationBaseType))
        {
            AddConstructors(typeSymbol, entryPoints);
            addedConstructor = true;
            AddMethodsByName(typeSymbol, new[] { "OnStart", "OnResume", "OnSleep", "CreateWindow" }, entryPoints);
        }

        if (InheritsFrom(typeSymbol, mauiPageBaseType))
        {
            if (!addedConstructor) AddConstructors(typeSymbol, entryPoints);
            addedConstructor = true;
            AddMethodsByName(typeSymbol, new[] { "OnAppearing", "OnDisappearing", "OnNavigatedTo", "OnNavigatingFrom" }, entryPoints);
        }

        if (InheritsFrom(typeSymbol, blazorComponentBaseType))
        {
            if (!addedConstructor) AddConstructors(typeSymbol, entryPoints);
            addedConstructor = true;
            AddMethodsByName(typeSymbol, new[] {
                    "SetParametersAsync",
                    "OnInitialized", "OnInitializedAsync",
                    "OnParametersSet", "OnParametersSetAsync",
                    "OnAfterRender", "OnAfterRenderAsync",
                    "BuildRenderTree"
                    }, entryPoints);
        }

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol methodSymbol)
            {
                if (methodSymbol.IsStatic &&
                    methodSymbol.Name == "CreateMauiApp" &&
                    typeSymbol.Name.Contains("MauiProgram"))
                {
                    entryPoints.Add(methodSymbol);
                }
            }
        }

    }

    private void AddMainMethod(Compilation compilation, HashSet<IMethodSymbol> entryPoints)
    {
        IMethodSymbol? mainMethod = compilation.GetEntryPoint(CancellationToken.None);

        if (mainMethod != null)
        {
            entryPoints.Add(mainMethod);
        }
    }

    private void AddConstructors(INamedTypeSymbol typeSymbol, HashSet<IMethodSymbol> entryPoints)
    {
        foreach (var constructor in typeSymbol.InstanceConstructors)
        {
            if (!constructor.IsStatic)
            {
                entryPoints.Add(constructor);
            }
        }
    }

    private void AddMethodsByName(INamedTypeSymbol typeSymbol, IEnumerable<string> names, HashSet<IMethodSymbol> entryPoints)
    {
        foreach (string name in names)
        {
            foreach (var method in typeSymbol.GetMembers(name).OfType<IMethodSymbol>())
            {
                entryPoints.Add(method);
            }
        }
    }

    private bool InheritsFrom(INamedTypeSymbol? typeSymbol, INamedTypeSymbol? potentialBaseType)
    {
        if (typeSymbol == null || potentialBaseType == null)
        {
            return false;
        }

        INamedTypeSymbol? current = typeSymbol;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, potentialBaseType))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
