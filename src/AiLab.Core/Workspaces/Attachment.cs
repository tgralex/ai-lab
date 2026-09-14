namespace AiLab.Core.Workspaces;

/// <summary>
/// Metadata for a file attached to a workspace. Bytes live on disk, content-addressed by hash
/// (under /data/attachments/&lt;sha256&gt;); the DB row is metadata only. SHA-256 is always computed
/// so experiments stay reproducible even if the original source file is later deleted.
/// </summary>
public sealed class Attachment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid WorkspaceId { get; init; }

    public required string Filename { get; init; }

    public required string StoredPath { get; init; }

    public required string MimeType { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
