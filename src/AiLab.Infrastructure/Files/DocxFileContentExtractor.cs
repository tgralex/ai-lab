using System.IO.Compression;
using System.Xml.Linq;

namespace AiLab.Infrastructure.Files;

/// <summary>
/// Extracts plain text from a .docx file. A .docx is a zip archive containing word/document.xml;
/// this reads that entry directly via System.IO.Compression/System.Xml.Linq (both part of the BCL,
/// no extra NuGet dependency needed).
/// </summary>
public sealed class DocxFileContentExtractor : IFileContentExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public bool CanHandle(string contentType, string fileName) =>
        contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(string absoluteFilePath, string contentType, CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(absoluteFilePath), cancellationToken);

    private static string ExtractText(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var body = document.Root?.Element(W + "body");
        if (body is null)
        {
            return string.Empty;
        }

        var paragraphs = body.Descendants(W + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(W + "t").Select(t => (string)t)))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, paragraphs);
    }
}
