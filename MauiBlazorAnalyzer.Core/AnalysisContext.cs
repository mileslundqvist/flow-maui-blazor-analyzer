using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;


namespace MauiBlazorAnalyzer.Core;

/// <summary>
/// Represents the context for code analysis
/// </summary>
public class AnalysisContext
{
    /// <summary>
    /// The project being analyzed
    /// </summary>
    public Project Project { get; }

    /// <summary>
    /// The project compilation
    /// </summary>
    public CompilationWithAnalyzers? Compilation { get; set; }

    /// <summary>
    /// Source documents in the project
    /// </summary>
    public ImmutableArray<Document> Documents { get; }

    /// <summary>
    /// Additional text documents in the project
    /// </summary>
    public ImmutableArray<TextDocument> AdditionalDocuments { get; }

    public AnalysisContext(Project project, ImmutableArray<Document> documents, ImmutableArray<TextDocument> additionalDocuments)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Documents = documents.IsDefault ? ImmutableArray<Document>.Empty : documents;
        AdditionalDocuments = additionalDocuments.IsDefault ? ImmutableArray<TextDocument>.Empty : additionalDocuments;
    }
}
