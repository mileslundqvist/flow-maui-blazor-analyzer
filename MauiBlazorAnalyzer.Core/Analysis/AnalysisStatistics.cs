namespace MauiBlazorAnalyzer.Core.Analysis;
/// <summary>
/// Statistics about the analysis run
/// </summary>
public class AnalysisStatistics
{
    public int TotalFilesAnalyzed { get; set; }
    public int CSharpFilesAnalyzed { get; set; }
    public int RazorFilesAnalyzed { get; set; }
    public TimeSpan AnalysisDuration { get; set; }

    public AnalysisStatistics()
    {
        TotalFilesAnalyzed = 0;
        CSharpFilesAnalyzed = 0;
        RazorFilesAnalyzed = 0;
        AnalysisDuration = TimeSpan.Zero;
    }

    /// <summary>
    /// Adds statistics from a single project analysis to the aggregate totals.
    /// </summary>
    /// <param name="projectStats">Statistics from one project.</param>
    public void Add(ProjectAnalysisStatistics projectStats)
    {
        ArgumentNullException.ThrowIfNull(projectStats);
        CSharpFilesAnalyzed += projectStats.CSharpFilesAnalyzed;
        RazorFilesAnalyzed += projectStats.RazorFilesAnalyzed;
    }
}
