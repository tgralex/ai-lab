using AiLab.Core.Requests;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class RequestEndpoints
{
    public record CreateRequestBody(
        string Name,
        string? Description,
        string ProviderId,
        string ModelId,
        string? SystemPrompt,
        string? CachedContextText,
        string? UserContextText,
        bool StreamingEnabled,
        int? MaxOutputTokens,
        string? ReasoningEffort,
        string? PromptCacheKey,
        string? StructuredOutputSchema,
        List<string>? Tags,
        List<Guid>? CachedContextAttachmentIds,
        List<Guid>? UserContextAttachmentIds);

    public static void MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces/{workspaceId:guid}/requests", async (Guid workspaceId, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Requests.Where(r => r.WorkspaceId == workspaceId).OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct)));

        app.MapPost("/api/workspaces/{workspaceId:guid}/requests", async (Guid workspaceId, CreateRequestBody body, AiLabDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.ProviderId) || string.IsNullOrWhiteSpace(body.ModelId))
            {
                return Results.BadRequest(new { error = "Name, ProviderId and ModelId are required." });
            }

            var request = new AiRequestDefinition
            {
                WorkspaceId = workspaceId,
                Name = body.Name,
                Description = body.Description,
                ProviderId = body.ProviderId,
                ModelId = body.ModelId,
                SystemPrompt = body.SystemPrompt,
                CachedContext = new ContentBlock { Text = body.CachedContextText ?? string.Empty, AttachmentIds = body.CachedContextAttachmentIds ?? [] },
                UserContext = new ContentBlock { Text = body.UserContextText ?? string.Empty, AttachmentIds = body.UserContextAttachmentIds ?? [] },
                StreamingEnabled = body.StreamingEnabled,
                MaxOutputTokens = body.MaxOutputTokens,
                Reasoning = string.IsNullOrEmpty(body.ReasoningEffort) ? null : new ReasoningConfig { Effort = body.ReasoningEffort },
                PromptCacheKey = body.PromptCacheKey,
                StructuredOutputSchema = body.StructuredOutputSchema,
                Tags = body.Tags ?? [],
                SortOrder = await NextSortOrderAsync(workspaceId, db, ct),
            };

            db.Requests.Add(request);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/requests/{request.Id}", request);
        });

        // Client sends the workspace's full task list in the user's desired order after a
        // drag-and-drop reorder; each request's SortOrder becomes its index in that list.
        app.MapPut("/api/workspaces/{workspaceId:guid}/requests/reorder", async (Guid workspaceId, List<Guid> orderedIds, AiLabDbContext db, CancellationToken ct) =>
        {
            var requests = await db.Requests.Where(r => r.WorkspaceId == workspaceId && orderedIds.Contains(r.Id)).ToListAsync(ct);
            var requestsById = requests.ToDictionary(r => r.Id);
            for (var i = 0; i < orderedIds.Count; i++)
            {
                if (requestsById.TryGetValue(orderedIds[i], out var request))
                {
                    request.SortOrder = i;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapGet("/api/requests/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var request = await db.Requests.FindAsync([id], ct);
            return request is null ? Results.NotFound() : Results.Ok(request);
        });

        app.MapPut("/api/requests/{id:guid}", async (Guid id, CreateRequestBody body, AiLabDbContext db, CancellationToken ct) =>
        {
            var request = await db.Requests.FindAsync([id], ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.ProviderId) || string.IsNullOrWhiteSpace(body.ModelId))
            {
                return Results.BadRequest(new { error = "Name, ProviderId and ModelId are required." });
            }

            request.Name = body.Name;
            request.Description = body.Description;
            request.ProviderId = body.ProviderId;
            request.ModelId = body.ModelId;
            request.SystemPrompt = body.SystemPrompt;
            request.CachedContext = new ContentBlock { Text = body.CachedContextText ?? string.Empty, AttachmentIds = body.CachedContextAttachmentIds ?? request.CachedContext.AttachmentIds };
            request.UserContext = new ContentBlock { Text = body.UserContextText ?? string.Empty, AttachmentIds = body.UserContextAttachmentIds ?? request.UserContext.AttachmentIds };
            request.StreamingEnabled = body.StreamingEnabled;
            request.MaxOutputTokens = body.MaxOutputTokens;
            request.Reasoning = string.IsNullOrEmpty(body.ReasoningEffort) ? null : new ReasoningConfig { Effort = body.ReasoningEffort };
            request.PromptCacheKey = body.PromptCacheKey;
            request.StructuredOutputSchema = body.StructuredOutputSchema;
            request.Tags = body.Tags ?? [];
            request.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(request);
        });

        app.MapDelete("/api/requests/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var request = await db.Requests.FindAsync([id], ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            db.Requests.Remove(request);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/api/requests/{id:guid}/clone", async (Guid id, string? newName, AiLabDbContext db, CancellationToken ct) =>
        {
            var original = await db.Requests.FindAsync([id], ct);
            if (original is null)
            {
                return Results.NotFound();
            }

            var clone = original.Clone(newName);
            clone.SortOrder = await NextSortOrderAsync(original.WorkspaceId, db, ct);
            db.Requests.Add(clone);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/requests/{clone.Id}", clone);
        });

        app.MapGet("/api/requests/{id:guid}/runs", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ExecutionRuns
                .Where(r => r.AiRequestId == id)
                .OrderByDescending(r => r.StartedAt)
                .ToListAsync(ct)));
    }

    private static async Task<int> NextSortOrderAsync(Guid workspaceId, AiLabDbContext db, CancellationToken ct)
    {
        var maxOrder = await db.Requests.Where(r => r.WorkspaceId == workspaceId).Select(r => (int?)r.SortOrder).MaxAsync(ct);
        return (maxOrder ?? -1) + 1;
    }
}
