using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;

public interface ITaintSink
{
    string Name { get; }
    string Severity { get; }
    bool Matches(string methodSignature);
}
