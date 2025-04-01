using Microsoft.CodeAnalysis;

namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Represents a diagnostic issue found during analysis.
/// </summary>
public class AnalysisDiagnostic
{
    public string Id { get; }
    public string Title { get; }
    public string Message { get; }
    public DiagnosticSeverity Severity { get; }
    public FileLinePositionSpan Location { get; }
    public string? FilePath => Location.Path;
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

    /// <summary>
    /// Creates an AnalysisDiagnostic from a Roslyn Diagnostic,
    /// correctly handling different location types.
    /// </summary>
    public static AnalysisDiagnostic FromRoslynDiagnostic(Diagnostic diagnostic)
    {
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));

        FileLinePositionSpan lineSpan;
        var location = diagnostic.Location;

        if (location == null || location.Kind == LocationKind.None)
        {
            // No location available
            lineSpan = new FileLinePositionSpan("None", default, default);
        }
        else if (location.IsInSource && location.SourceTree != null)
        {
            // Location is in a source file we have access to
            lineSpan = location.GetMappedLineSpan();
        }
        else if (location.Kind == LocationKind.ExternalFile)
        {
            lineSpan = location.GetLineSpan();
        }
        else
        {
            string path = location.SourceTree?.FilePath ?? location.MetadataModule?.Name ?? "Unknown";
            lineSpan = new FileLinePositionSpan(Path.GetFileName(path), default, default);
        }


        return new AnalysisDiagnostic(
            diagnostic.Id,
            diagnostic.Descriptor.Title.ToString(),
            diagnostic.GetMessage(),
            diagnostic.Severity,
            lineSpan,
            diagnostic.Descriptor.HelpLinkUri);
    }
}

