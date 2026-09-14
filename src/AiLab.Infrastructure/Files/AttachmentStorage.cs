using AiLab.Core.Workspaces;

namespace AiLab.Infrastructure.Files;

/// <summary>
/// Stores attachment bytes content-addressed under &lt;dataDir&gt;/attachments/&lt;sha256&gt;. The DB
/// row (see Persistence) holds only metadata + hash. Dedup is free — two identical files share one
/// blob on disk.
/// </summary>
public sealed class AttachmentStorage
{
    private readonly string _attachmentsDirectory;

    public AttachmentStorage(string dataDirectory)
    {
        _attachmentsDirectory = Path.Combine(dataDirectory, "attachments");
        Directory.CreateDirectory(_attachmentsDirectory);
    }

    public async Task<Attachment> StoreAsync(Guid workspaceId, string originalFilename, string mimeType, byte[] content, CancellationToken ct = default)
    {
        var sha256 = FileHasher.ComputeSha256(content);
        var storedPath = Path.Combine(_attachmentsDirectory, sha256);

        if (!File.Exists(storedPath))
        {
            await AtomicFileWriter.WriteAllBytesAsync(storedPath, content, ct);
        }

        return new Attachment
        {
            WorkspaceId = workspaceId,
            Filename = originalFilename,
            StoredPath = storedPath,
            MimeType = mimeType,
            SizeBytes = content.LongLength,
            Sha256 = sha256,
        };
    }

    public string GetAbsolutePath(string sha256) => Path.Combine(_attachmentsDirectory, sha256);

    public bool Exists(string sha256) => File.Exists(GetAbsolutePath(sha256));
}
