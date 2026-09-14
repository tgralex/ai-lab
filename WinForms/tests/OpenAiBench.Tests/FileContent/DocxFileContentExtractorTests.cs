using System.IO.Compression;
using OpenAiBench.Infrastructure.FileContent;
using Xunit;

namespace OpenAiBench.Tests.FileContent;

public class DocxFileContentExtractorTests : IDisposable
{
    private const string DocumentXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p><w:r><w:t>Alice Example</w:t></w:r></w:p>
            <w:p><w:r><w:t>Senior Engineer with 10 years experience.</w:t></w:r></w:p>
          </w:body>
        </w:document>
        """;

    private readonly string _docxPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.docx");

    public DocxFileContentExtractorTests()
    {
        using var archive = ZipFile.Open(_docxPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(DocumentXml);
    }

    [Fact]
    public void CanHandle_DocxExtension_ReturnsTrue()
    {
        var extractor = new DocxFileContentExtractor();

        Assert.True(extractor.CanHandle("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "resume.docx"));
    }

    [Fact]
    public void CanHandle_NonDocxFile_ReturnsFalse()
    {
        var extractor = new DocxFileContentExtractor();

        Assert.False(extractor.CanHandle("text/plain", "notes.txt"));
    }

    [Fact]
    public async Task ExtractTextAsync_ReadsParagraphTextFromDocumentXml()
    {
        var extractor = new DocxFileContentExtractor();

        var text = await extractor.ExtractTextAsync(_docxPath, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        Assert.Contains("Alice Example", text);
        Assert.Contains("Senior Engineer with 10 years experience.", text);
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath))
        {
            File.Delete(_docxPath);
        }
    }
}
