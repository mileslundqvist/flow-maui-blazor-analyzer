using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Statistics about the analysis run
/// </summary>
public class AnalysisStatistics
{
    public int TotalFiles { get; set; }
    public int RazorFiles { get; set; }
    public int CSharpFiles { get; set; }
    public int LinesOfCode { get; set; }
    public TimeSpan AnalysisDuration { get; set; }
}
