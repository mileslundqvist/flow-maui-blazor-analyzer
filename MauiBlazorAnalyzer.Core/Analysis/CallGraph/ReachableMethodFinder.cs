using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Analysis.CallGraph;
public sealed class ReachableMethodFinder
{
    private const string MauiShellTypeName = "Microsoft.Maui.Controls.Shell";
    private const string GoToAsyncMethodName = "GoToAsync";

    public HashSet<IMethodSymbol> FindReachableMethods(
        Compilation compilation,
        CallGraph callGraph,
        HashSet<IMethodSymbol> initialEntryPoints)
    {
        var reachableMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var worklist = new Queue<IMethodSymbol>();

        foreach (var entryPoint in initialEntryPoints)
        {
            if (entryPoint == null) continue;

            bool isApplicationCode = entryPoint.ContainingAssembly?.Name?.StartsWith(compilation.AssemblyName) ?? false;
            bool isEssentialEntryPoint = IsEssentialNonAppEntryPoint(entryPoint);

            if (isApplicationCode || isEssentialEntryPoint)
            {
                if (reachableMethods.Add(entryPoint))
                {
                    worklist.Enqueue(entryPoint);
                }
            }
        }

        while (worklist.Count > 0)
        {
            IMethodSymbol currentMethod = worklist.Dequeue();

            SimulateFrameworkTransitions(compilation, currentMethod, reachableMethods, worklist);

            //foreach (var callee in callGraph.GetCallees(currentMethod))
            //{
            //    if (callee != null && reachableMethods.Add(callee))
            //    {
            //        worklist.Enqueue(callee);
            //    }
            //}
        }

        return reachableMethods;
    }

    private bool IsEssentialNonAppEntryPoint(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.Name?.Contains("MauiProgram") == true && methodSymbol.Name == "CreateMauiApp";
    }

    private void SimulateFrameworkTransitions(
        Compilation compilation,
        IMethodSymbol currentMethod,
        HashSet<IMethodSymbol> reachableMethods,
        Queue<IMethodSymbol> worklist)
    {

        if (currentMethod.Name == GoToAsyncMethodName &&
            InheritsFrom(currentMethod.ContainingType, MauiShellTypeName, compilation))
        {
            string targetPageName = ExtractNavigationTarget(compilation, currentMethod);

            if (!string.IsNullOrEmpty(targetPageName))
            {
                INamedTypeSymbol? targetPageType = FindApplicationTypeByName(compilation, targetPageName);

                if (targetPageType != null)
                {
                    AddConstructorsAndLifecycle(targetPageType, reachableMethods, worklist);
                }
            }
        }
    }

    private string ExtractNavigationTarget(Compilation compilation, IMethodSymbol navigationMethod)
    {
        string target = string.Empty;
        foreach (var syntaxRef in navigationMethod.DeclaringSyntaxReferences)
        {
            SemanticModel? semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            if (semanticModel == null) continue;

            SyntaxNode? node = syntaxRef.GetSyntax();
            var invocationNodes = node.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocationNodes)
            {
                IMethodSymbol? invokedSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (SymbolEqualityComparer.Default.Equals(invokedSymbol?.OriginalDefinition, navigationMethod.OriginalDefinition) ||
                   (invokedSymbol != null && invokedSymbol.Name == GoToAsyncMethodName && InheritsFrom(invokedSymbol.ContainingType, MauiShellTypeName, compilation))) // Check again to be sure
                {
                    if (invocation.ArgumentList.Arguments.Count > 0 &&
                        invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                        literal.Token.ValueText != null)
                    {
                        string route = literal.Token.ValueText;
                        target = route.TrimStart('/'); // Basic cleanup
                        int queryIndex = target.IndexOf('?');
                        if (queryIndex > 0) target = target.Substring(0, queryIndex); // Remove query string

                        return target; // Found first literal argument
                    }
                }
            }
        }
        return target; // Return empty if not found
    }

    private INamedTypeSymbol? FindApplicationTypeByName(Compilation compilation, string typeName)
    {
        return compilation.GlobalNamespace
            .GetMembers()
            .SelectMany(ns => GetAllTypes(ns))
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(t => t.Name == typeName && (t.ContainingAssembly?.Name?.StartsWith(compilation.AssemblyName) ?? false));
    }

    private IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol container)
    {
        if (container is INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceOrTypeSymbol memberContainer)
                {
                    foreach (var type in GetAllTypes(memberContainer))
                    {
                        yield return type;
                    }
                }
            }
        }
        else if (container is INamedTypeSymbol type)
        {
            yield return type;
            foreach (var nestedType in type.GetTypeMembers())
            {
                foreach (var nested in GetAllTypes(nestedType))
                {
                    yield return nested;
                }
            }
        }
    }


    private void AddConstructorsAndLifecycle(
        INamedTypeSymbol pageType,
        HashSet<IMethodSymbol> reachableMethods,
        Queue<IMethodSymbol> worklist)
    {
        foreach (var constructor in pageType.InstanceConstructors)
        {
            if (!constructor.IsStatic && reachableMethods.Add(constructor))
            {
                worklist.Enqueue(constructor);
            }
        }

        var appearingMethod = pageType.GetMembers("OnAppearing").OfType<IMethodSymbol>().FirstOrDefault();
        if (appearingMethod != null && reachableMethods.Add(appearingMethod))
        {
            worklist.Enqueue(appearingMethod);
        }

        var navigatedToMethod = pageType.GetMembers("OnNavigatedTo").OfType<IMethodSymbol>().FirstOrDefault();
        if (navigatedToMethod != null && reachableMethods.Add(navigatedToMethod))
        {
            worklist.Enqueue(navigatedToMethod);
        }
    }

    private bool InheritsFrom(INamedTypeSymbol? typeSymbol, string baseTypeName, Compilation compilation)
    {
        if (typeSymbol == null) return false;
        INamedTypeSymbol? potentialBaseType = compilation.GetTypeByMetadataName(baseTypeName);
        if (potentialBaseType == null) return false;

        INamedTypeSymbol? current = typeSymbol;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, potentialBaseType.OriginalDefinition))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
