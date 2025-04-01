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
    public int TotalFilesAnalyzed { get; set; }
    public int CSharpFilesAnalyzed { get; set; }
    public int RazorFilesAnalyzed { get; set; }
    public int LinesOfCode { get; set; }
    public TimeSpan AnalysisDuration { get; set; }
}
