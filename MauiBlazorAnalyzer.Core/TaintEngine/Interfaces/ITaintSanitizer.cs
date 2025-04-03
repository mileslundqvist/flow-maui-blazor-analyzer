using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine.Interfaces;

public interface ITaintSanitizer
{
    string Name { get; }
    string TaintType { get; }
    bool Matches(string methodSignature);
}
