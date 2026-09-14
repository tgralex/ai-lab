namespace AiLab.Infrastructure.Files;

/// <summary>Write-to-temp-then-move pattern (near-atomic on NTFS) — ported from the WinForms AtomicFileWriter.</summary>
public static class AtomicFileWriter
{
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        await File.WriteAllBytesAsync(tempPath, content, ct);
        File.Move(tempPath, path, overwrite: true);
    }

    public static async Task WriteAllTextAsync(string path, string content, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(tempPath, content, ct);
        File.Move(tempPath, path, overwrite: true);
    }
}
