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
    private Dictionary<IMethodSymbol, List<IMethodSymbol>> callGraph;

    public CallGraphGenerator(Project project, Compilation compilation)
    {
        _compilation = compilation;
        _project = project;
        callGraph = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
    }

    public async Task CreateCallGraph()
    {
        var allMethods = _compilation.SyntaxTrees.Select(tree => _compilation.GetSemanticModel(tree))
            .SelectMany(model =>
            {
                var methodDeclarations = model.SyntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                return methodDeclarations.Select(methodDeclaration => model.GetDeclaredSymbol(methodDeclaration)).OfType<IMethodSymbol>().Where(symbol => symbol != null);
            }).ToList();
        //var searchScope = ImmutableHashSet.Create<Document>(_project.Documents.ToArray());

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

            
            var references = await SymbolFinder.FindCallersAsync(methodSymbol, solution, /*searchScope,*/ default);

            // Iterate through all the callers of the method
            foreach (var caller in references)
            {
                var callerSymbol = caller.CallingSymbol as IMethodSymbol;
                if (callerSymbol == null)
                    continue;

                if (!callGraph.ContainsKey(callerSymbol))
                {
                    callGraph[callerSymbol] = new List<IMethodSymbol>();

                }

                callGraph[callerSymbol].Add(methodSymbol);

            }
        }
    }

    public Dictionary<IMethodSymbol, List<IMethodSymbol>> GetCallGraph()
    {
        return callGraph;
        
    }

    public void PrintCallGraph(Dictionary<IMethodSymbol, List<IMethodSymbol>> callGraph)
    {
        foreach (var kvp in callGraph)
        {
            var caller = kvp.Key;
            var callees = kvp.Value;
            Console.WriteLine($"{caller.Name} calls:");
            foreach (var callee in callees)
            {
                Console.WriteLine($"  - {callee.Name}");
            }
        }
    }
}
