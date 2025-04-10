using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.Summary;

public class RegisterFacts
{
    public RegisterFacts()
    {
        var factInputTainted = TaintDomainRegistry.GetOrCreateFact("Tainted(Input)");
        var factSinkUsed = TaintDomainRegistry.GetOrCreateFact("SinkUsed");
        var factCleanReturn = TaintDomainRegistry.GetOrCreateFact("CleanReturn");
    }
}
