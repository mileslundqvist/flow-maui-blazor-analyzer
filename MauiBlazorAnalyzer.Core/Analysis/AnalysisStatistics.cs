namespace MauiBlazorAnalyzer.Core.Analysis;
/// <summary>
/// Statistics about the analysis run
/// </summary>
public class AnalysisStatistics
{
    public TimeSpan AnalysisDuration { get; set; }

    public AnalysisStatistics()
    {
        AnalysisDuration = TimeSpan.Zero;
    }
}
