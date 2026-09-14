using AiLab.Infrastructure.Files;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class AttachmentEndpoints
{
    private const long MaxUploadBytes = 25 * 1024 * 1024; // 25 MB — generous for resumes/job descriptions, not a general file store

    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces/{workspaceId:guid}/attachments", async (Guid workspaceId, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Attachments
                .Where(a => a.WorkspaceId == workspaceId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct)));

        app.MapPost("/api/workspaces/{workspaceId:guid}/attachments", async (
            Guid workspaceId,
            IFormFile file,
            AttachmentStorage storage,
            AiLabDbContext db,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { error = "File is empty." });
            }

            if (file.Length > MaxUploadBytes)
            {
                return Results.BadRequest(new { error = $"File exceeds the {MaxUploadBytes / (1024 * 1024)}MB limit." });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);

            var mimeType = string.IsNullOrEmpty(file.ContentType) || file.ContentType == "application/octet-stream"
                ? ContentTypeGuesser.Guess(file.FileName)
                : file.ContentType;

            var attachment = await storage.StoreAsync(workspaceId, file.FileName, mimeType, ms.ToArray(), ct);
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/attachments/{attachment.Id}", attachment);
        }).DisableAntiforgery();

        app.MapDelete("/api/attachments/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var attachment = await db.Attachments.FindAsync([id], ct);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            // Metadata only — the content-addressed blob on disk may be shared by other attachment
            // rows with the same hash, so it's left in place (harmless orphaned bytes at worst).
            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
