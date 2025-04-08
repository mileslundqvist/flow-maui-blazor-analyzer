using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.CallGraph;

public class CallGraphGenerator
{
    private readonly Compilation _compilation;
    private readonly Project _project;
    private readonly Solution _solution;

    public CallGraphGenerator(Project project, Compilation compilation)
    {
        _compilation = compilation;
        _project = project;
        _solution = project.Solution;
    }

    public async Task<CallGraph> CreateCallGraphAsync()
    {
        CallGraph callGraph = new();

        var allMethods = _compilation.SyntaxTrees.Select(tree => _compilation.GetSemanticModel(tree))
            .SelectMany(model =>
            {
                var methodDeclarations = model.SyntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                return methodDeclarations.Select(methodDeclaration => model.GetDeclaredSymbol(methodDeclaration)).OfType<IMethodSymbol>().Where(symbol => symbol != null);
            }).ToList();

        // Iterate through all methods and find their callers
        foreach (var methodSymbol in allMethods)
        {
            // Feels like a bit hacky but it works currently, might be a problem for larger solutions
            var solution = _project.Solution;
            var projectsToRemove = solution.Projects.Where(p => p.AssemblyName == _project.AssemblyName && !p.Name.Contains("android"));
            
            foreach (var projectToRemove in projectsToRemove)
            {
                solution = solution.RemoveProject(projectToRemove.Id);
            }
            
            var references = await SymbolFinder.FindCallersAsync(methodSymbol, solution, default);

            // Iterate through all the callers of the method
            foreach (var caller in references)
            {
                if (caller.CallingSymbol != null && caller.CallingSymbol is IMethodSymbol callerSymbol)
                {
                    callGraph.AddEdge(callerSymbol, methodSymbol);
                }

            }
        }

        return callGraph;
    }
}
