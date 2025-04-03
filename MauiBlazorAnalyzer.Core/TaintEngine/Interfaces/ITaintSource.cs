using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;

public interface ITaintSource
{
    string Name { get; }
    bool Matches(string methodSignature);
}
