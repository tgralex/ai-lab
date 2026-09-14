namespace AiLab.Infrastructure.Files;

/// <summary>Handles text-like files by reading them verbatim.</summary>
public sealed class TextFileContentExtractor : IFileContentExtractor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".xml", ".html", ".htm", ".log", ".yaml", ".yml",
    };

    public bool CanHandle(string contentType, string fileName) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        contentType is "application/json" or "application/xml" ||
        TextExtensions.Contains(Path.GetExtension(fileName));

    public Task<string> ExtractTextAsync(string absoluteFilePath, string contentType, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(absoluteFilePath, cancellationToken);
}
