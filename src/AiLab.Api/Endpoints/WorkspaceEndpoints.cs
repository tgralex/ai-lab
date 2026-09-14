using AiLab.Core.Workspaces;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public record CreateWorkspaceRequest(string Name, string? Description, List<string>? Tags);

    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspaces");

        group.MapGet("/", async (AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Workspaces.OrderByDescending(w => w.UpdatedAt).ToListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var workspace = await db.Workspaces.FindAsync([id], ct);
            return workspace is null ? Results.NotFound() : Results.Ok(workspace);
        });

        group.MapPost("/", async (CreateWorkspaceRequest request, AiLabDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Name is required." });
            }

            var workspace = new Workspace { Name = request.Name, Description = request.Description, Tags = request.Tags ?? [] };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/workspaces/{workspace.Id}", workspace);
        });

        group.MapPut("/{id:guid}", async (Guid id, CreateWorkspaceRequest request, AiLabDbContext db, CancellationToken ct) =>
        {
            var workspace = await db.Workspaces.FindAsync([id], ct);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Name is required." });
            }

            workspace.Name = request.Name;
            workspace.Description = request.Description;
            workspace.Tags = request.Tags ?? [];
            workspace.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(workspace);
        });

        group.MapDelete("/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var workspace = await db.Workspaces.FindAsync([id], ct);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            db.Workspaces.Remove(workspace);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/{workspaceId:guid}/variables", async (Guid workspaceId, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.WorkspaceVariables.Where(v => v.WorkspaceId == workspaceId).ToListAsync(ct)));

        group.MapPut("/{workspaceId:guid}/variables/{name}", async (Guid workspaceId, string name, SetVariableRequest request, AiLabDbContext db, CancellationToken ct) =>
        {
            var existing = await db.WorkspaceVariables.FirstOrDefaultAsync(v => v.WorkspaceId == workspaceId && v.Name == name, ct);
            if (existing is null)
            {
                db.WorkspaceVariables.Add(new WorkspaceVariable { WorkspaceId = workspaceId, Name = name, Value = request.Value });
            }
            else
            {
                existing.Value = request.Value;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public record SetVariableRequest(string Value);
}
