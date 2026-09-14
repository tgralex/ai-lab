using OpenAiBench.Core.Ports;

namespace OpenAiBench.Infrastructure.FileContent;

/// <summary>
/// Handles text-like files by reading them verbatim. PDF/DOCX extraction is deferred past
/// milestone 1 — a future implementation of <see cref="IFileContentExtractor"/> can be registered
/// alongside this one without touching any calling code.
/// </summary>
public sealed class TextFileContentExtractor : IFileContentExtractor
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".xml", ".html", ".htm", ".log", ".yaml", ".yml"
    };

    public bool CanHandle(string contentType, string fileName) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        contentType is "application/json" or "application/xml" ||
        TextExtensions.Contains(Path.GetExtension(fileName));

    public Task<string> ExtractTextAsync(string absoluteFilePath, string contentType, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(absoluteFilePath, cancellationToken);
}
