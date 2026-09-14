using AiLab.Core.Execution;

namespace AiLab.Tests.TestDoubles;

/// <summary>No attachments by default — tests that need attachment content set <see cref="Contents"/> explicitly.</summary>
public sealed class FakeAttachmentContentProvider : IAttachmentContentProvider
{
    public Dictionary<Guid, AttachmentContent> Contents { get; } = [];

    public Task<AttachmentContent?> GetContentAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Contents.GetValueOrDefault(attachmentId));
}
