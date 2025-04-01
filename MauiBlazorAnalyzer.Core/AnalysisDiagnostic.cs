using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Represents a diagnostic issue found during analysis
/// </summary>
public class AnalysisDiagnostic
{
    public string Id { get; }
    public string Title { get; }
    public string Message { get; }
    public DiagnosticSeverity Severity { get; }
    public FileLinePositionSpan Location { get; }
    public string? HelpLink { get; }

    public AnalysisDiagnostic(
        string id,
        string title,
        string message,
        DiagnosticSeverity severity,
        FileLinePositionSpan location,
        string? helpLink = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
        Location = location;
        HelpLink = helpLink;
    }

    public static AnalysisDiagnostic FromDiagnostic(Diagnostic diagnostic)
    {
        return new AnalysisDiagnostic(
            diagnostic.Id,
            diagnostic.Descriptor.Title.ToString(),
            diagnostic.GetMessage(),
            diagnostic.Severity,
            diagnostic.Location.GetLineSpan(),
            diagnostic.Descriptor.HelpLinkUri);
    }
}

