using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core.TaintEngine;

public enum TaintState
{
    /// <summary>
    /// Represents a state where the taint is not present.
    /// </summary>
    NotTainted,
    /// <summary>
    /// Represents a state where the taint is present.
    /// </summary>
    Tainted,
    /// <summary>
    /// Represents a state where the taint is unknown.
    /// </summary>
    Unknown
}