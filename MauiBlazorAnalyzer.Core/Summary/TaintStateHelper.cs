using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Summary;

public static class TaintStateHelper
{
    // Adds a fact to the state.
    public static int AddFact(int state, TaintFact fact)
    {
        return state | (1 << fact.Index);
    }

    // Removes a fact from the state.
    public static int RemoveFact(int state, TaintFact fact)
    {
        return state & ~(1 << fact.Index);
    }

    // Checks if a fact is present in the state.
    public static bool HasFact(int state, TaintFact fact)
    {
        return (state & (1 << fact.Index)) != 0;
    }

    // Returns the union of two states.
    public static int Union(int state1, int state2)
    {
        return state1 | state2;
    }

    // For debugging: prints out the names of all facts in the state.
    public static void PrintState(int state)
    {
        Console.WriteLine("Current Taint Facts:");
        foreach (var fact in TaintDomainRegistry.GetAllFacts())
        {
            if (HasFact(state, fact))
            {
                Console.WriteLine($" - {fact}");
            }
        }
    }
}
