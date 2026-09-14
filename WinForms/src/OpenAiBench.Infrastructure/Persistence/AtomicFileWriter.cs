namespace OpenAiBench.Infrastructure.Persistence;

/// <summary>
/// Writes a file by first writing to a temp file in the same directory, then renaming it over the
/// target. On NTFS, same-volume <see cref="File.Move(string,string,bool)"/> is effectively atomic,
/// so a crash mid-write leaves the original file untouched instead of a half-written target.
/// </summary>
public static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(string targetPath, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new ArgumentException($"Path '{targetPath}' has no directory component.", nameof(targetPath));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(targetPath)}.tmp-{Guid.NewGuid():N}");

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }
}
