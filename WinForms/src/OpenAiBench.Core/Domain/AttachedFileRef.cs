namespace OpenAiBench.Core.Domain;

public sealed class AttachedFileRef
{
    public required string Id { get; init; }
    public required string OriginalFileName { get; init; }
    public required string StoredRelativePath { get; init; }
    public long SizeBytes { get; init; }
    public required string ContentType { get; init; }
    public required string Sha256 { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}
