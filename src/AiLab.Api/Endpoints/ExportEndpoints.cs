using AiLab.Core.Statistics;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workspaces/{workspaceId:guid}/export", async (
            Guid workspaceId,
            string? format,
            AiLabDbContext db,
            CancellationToken ct) =>
        {
            var workspace = await db.Workspaces.FindAsync([workspaceId], ct);
            if (workspace is null)
            {
                return Results.NotFound();
            }

            var requests = await db.Requests.Where(r => r.WorkspaceId == workspaceId).ToListAsync(ct);
            var requestsById = requests.ToDictionary(r => r.Id);
            var requestIds = requests.Select(r => r.Id).ToList();

            var runs = await db.ExecutionRuns.Where(r => requestIds.Contains(r.AiRequestId)).OrderByDescending(r => r.StartedAt).ToListAsync(ct);

            var rows = runs.Select(run => new BenchmarkExportRow
            {
                Workspace = workspace.Name,
                ExecutionPlan = null,
                Request = requestsById.TryGetValue(run.AiRequestId, out var req) ? req.Name : run.AiRequestId.ToString(),
                RequestId = run.AiRequestId,
                Run = run.Id,
                Provider = run.ProviderId,
                Model = run.ActualModel ?? run.RequestedModel ?? "",
                Reasoning = requestsById.TryGetValue(run.AiRequestId, out var req2) ? req2.Reasoning?.Effort : null,
                StartedAt = run.StartedAt,
                TotalMs = run.TotalDuration?.TotalMilliseconds,
                TtftMs = run.TimeToFirstOutputToken?.TotalMilliseconds,
                GenerationMs = run.GenerationDuration?.TotalMilliseconds,
                InputTokens = run.Usage.InputTokens,
                CachedTokens = run.Usage.CachedInputTokens,
                OutputTokens = run.Usage.OutputTokens,
                ReasoningTokens = run.Usage.ReasoningTokens,
                TokensPerSecond = run.OutputTokensPerSecond,
                EstimatedCost = run.EstimatedTotalCost,
                ActualCost = run.EstimatedTotalCost,
                Status = run.Status.ToString(),
            }).ToList();

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = CsvExporter.Export(rows);
                return Results.Text(csv, "text/csv", System.Text.Encoding.UTF8);
            }

            var json = JsonExporter.Export(rows);
            return Results.Text(json, "application/json", System.Text.Encoding.UTF8);
        });
    }
}
