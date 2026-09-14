namespace AiLab.Core.Execution;

public sealed class AttachmentContent
{
    public required string Sha256 { get; init; }

    public required string Filename { get; init; }

    /// <summary>Best-effort extracted text. Null if the file type isn't supported for inlining (e.g. an unrecognized binary format).</summary>
    public string? ExtractedText { get; init; }
}

/// <summary>
/// Resolves an attachment id to its content for inlining into a prompt. Implemented in
/// Infrastructure (needs DB + filesystem access) — Core only depends on this abstraction so
/// AiRequestExecutor stays free of I/O, matching how IAiProvider is injected.
/// </summary>
public interface IAttachmentContentProvider
{
    Task<AttachmentContent?> GetContentAsync(Guid attachmentId, CancellationToken cancellationToken);
}
