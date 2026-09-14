using AiLab.Core.Execution;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Infrastructure.Files;

/// <summary>Implements Core's IAttachmentContentProvider — looks up the Attachment row, reads the file from disk, and extracts text via the registered IFileContentExtractors.</summary>
public sealed class AttachmentContentProvider(AiLabDbContext db, IEnumerable<IFileContentExtractor> extractors) : IAttachmentContentProvider
{
    public async Task<AttachmentContent?> GetContentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null || !File.Exists(attachment.StoredPath))
        {
            return null;
        }

        var extractor = extractors.FirstOrDefault(e => e.CanHandle(attachment.MimeType, attachment.Filename));
        var extractedText = extractor is null
            ? null
            : await extractor.ExtractTextAsync(attachment.StoredPath, attachment.MimeType, cancellationToken);

        return new AttachmentContent
        {
            Sha256 = attachment.Sha256,
            Filename = attachment.Filename,
            ExtractedText = extractedText,
        };
    }
}
