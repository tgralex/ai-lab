using OpenAiBench.Infrastructure.Persistence;
using Xunit;

namespace OpenAiBench.Tests.Persistence;

public class FileHasherTests
{
    [Fact]
    public async Task ComputeSha256Async_KnownContent_MatchesKnownHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "hello");

            var hash = await FileHasher.ComputeSha256Async(path);

            Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_DifferentContent_ProducesDifferentHashes()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(pathA, "content A");
            await File.WriteAllTextAsync(pathB, "content B");

            var hashA = await FileHasher.ComputeSha256Async(pathA);
            var hashB = await FileHasher.ComputeSha256Async(pathB);

            Assert.NotEqual(hashA, hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }
}
