using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Interprocedural;
public interface IInterproceduralCFG<N,M>
{
    // Returns the successors of a given node
    IEnumerable<N> GetSuccessors(N node);

    // Returns call sites in a method (or within a given node)
    IEnumerable<N> GetCallSites(M method);

    // Maps a call site to its potential callee methods based on semantic analysis
    IEnumerable<M> GetCalleesAtCallSite(N callSite);

    // For a given method and call site, returns the exit nodes (returns)
    IEnumerable<N> GetExitNodes(M callee);

    // Returns the return sites corresponding to a call site
    IEnumerable<N> GetReturnSites(N callSite);
}
