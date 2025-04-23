using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace MauiBlazorAnalyzer.Core.Interprocedural.DB;
internal sealed class TaintSpecDB
{
    internal class TaintSpecFileContent
    {
        public List<string>? Data { get; set; }
    }

    public static TaintSpecDB Instance { get; } = new();

    public bool IsSource(IMethodSymbol m) => m != null && _sources.Contains(m.OriginalDefinition.ToDisplayString());
    public bool IsSink(IMethodSymbol m) => m != null && _sinks.Contains(m.OriginalDefinition.ToDisplayString());
    public bool IsSanitizer(IMethodSymbol m) => m != null && _sanitizers.Contains(m.OriginalDefinition.ToDisplayString());

    private readonly HashSet<string> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sinks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sanitizers = new(StringComparer.Ordinal);
    
    private TaintSpecDB() 
    {

        string assemblyLocation = AppContext.BaseDirectory;

        string sinksFilePath = Path.Combine(assemblyLocation, "Interprocedural", "DB", "sinks-spec.json");
        string sourcesFilePath = Path.Combine(assemblyLocation, "Interprocedural", "DB", "sources-spec.json");
        string sanitizersFilePath = Path.Combine(assemblyLocation, "Interprocedural", "DB", "sanitizers-spec.json");

        Add(sinksFilePath, _sinks);
        Add(sourcesFilePath, _sources);
        Add(sanitizersFilePath, _sanitizers);

    }

    private void Add(string FilePath, ISet<string> set)
    {
        try
        {
            if (File.Exists(FilePath)) 
            {
                string jsonContent = File.ReadAllText(FilePath);
                var specContent = JsonSerializer.Deserialize<TaintSpecFileContent>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (specContent != null)
                {
                    if (specContent.Data != null)
                    {
                        set.UnionWith(specContent.Data);
                    }
                }
            }
        }
        catch (JsonException jsonEx)
        {
            Console.Error.WriteLine($"Error parsing taint specification file {FilePath}: {jsonEx.Message}");
            // Handle error, maybe throw or continue with empty sets
        }
        catch (IOException ioEx)
        {
            Console.Error.WriteLine($"Error reading taint specification file {FilePath}: {ioEx.Message}");
            // Handle error
        }
        catch (System.Exception ex) // Catch other potential errors
        {
            Console.Error.WriteLine($"Unexpected error loading taint specification from {FilePath}: {ex.Message}");
            // Handle error
        }
    }
}
