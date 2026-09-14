using System.Security.Cryptography;

namespace AiLab.Infrastructure.Files;

/// <summary>Ported from the WinForms FileHasher.</summary>
public static class FileHasher
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hashBytes);
    }

    public static string ComputeSha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
