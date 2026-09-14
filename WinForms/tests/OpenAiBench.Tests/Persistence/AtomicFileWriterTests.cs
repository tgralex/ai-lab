using OpenAiBench.Infrastructure.Persistence;
using Xunit;

namespace OpenAiBench.Tests.Persistence;

public class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "OpenAiBenchTests_" + Guid.NewGuid());

    [Fact]
    public async Task WriteAllTextAsync_WritesContentAndLeavesNoTempFileBehind()
    {
        var targetPath = Path.Combine(_tempDirectory, "data.json");

        await AtomicFileWriter.WriteAllTextAsync(targetPath, "{\"value\":1}");

        Assert.True(File.Exists(targetPath));
        Assert.Equal("{\"value\":1}", await File.ReadAllTextAsync(targetPath));
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp-*"));
    }

    [Fact]
    public async Task WriteAllTextAsync_Overwrite_ReplacesPreviousContent()
    {
        var targetPath = Path.Combine(_tempDirectory, "data.json");
        await AtomicFileWriter.WriteAllTextAsync(targetPath, "first");

        await AtomicFileWriter.WriteAllTextAsync(targetPath, "second");

        Assert.Equal("second", await File.ReadAllTextAsync(targetPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
