namespace AiLab.Core.Requests;

/// <summary>A block of prompt content that can mix free text with attached files (by attachment id).</summary>
public sealed class ContentBlock
{
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}
