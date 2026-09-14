using AiLab.Core.Statistics;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class ComparisonEndpoints
{
    public record ComparisonRow(
        Guid RequestId,
        string RequestName,
        string Provider,
        string Model,
        string? Reasoning,
        Guid? RunId,
        double? TotalMs,
        double? TtftMs,
        double? GenerationMs,
        int InputTokens,
        int CachedTokens,
        double CachePercent,
        int OutputTokens,
        int ReasoningTokens,
        double? TokensPerSecond,
        decimal? Cost,
        string Status);

    public record DiffRequestBody(string Left, string Right);

    public static void MapComparisonEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/comparison", async (string requestIds, AiLabDbContext db, CancellationToken ct) =>
        {
            var ids = requestIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Guid.Parse)
                .ToList();

            var rows = new List<ComparisonRow>();
            foreach (var id in ids)
            {
                var request = await db.Requests.FindAsync([id], ct);
                if (request is null)
                {
                    continue;
                }

                var latestRun = await db.ExecutionRuns
                    .Where(r => r.AiRequestId == id)
                    .OrderByDescending(r => r.StartedAt)
                    .FirstOrDefaultAsync(ct);

                rows.Add(new ComparisonRow(
                    request.Id,
                    request.Name,
                    request.ProviderId,
                    request.ModelId,
                    request.Reasoning?.Effort,
                    latestRun?.Id,
                    latestRun?.TotalDuration?.TotalMilliseconds,
                    latestRun?.TimeToFirstOutputToken?.TotalMilliseconds,
                    latestRun?.GenerationDuration?.TotalMilliseconds,
                    latestRun?.Usage.InputTokens ?? 0,
                    latestRun?.Usage.CachedInputTokens ?? 0,
                    latestRun?.Usage.CacheHitPercentage ?? 0,
                    latestRun?.Usage.OutputTokens ?? 0,
                    latestRun?.Usage.ReasoningTokens ?? 0,
                    latestRun?.OutputTokensPerSecond,
                    latestRun?.EstimatedTotalCost,
                    latestRun?.Status.ToString() ?? "NoRuns"));
            }

            return Results.Ok(rows);
        });

        app.MapPost("/api/diff", (DiffRequestBody body) =>
        {
            try
            {
                var jsonDiff = JsonDiffer.Diff(body.Left, body.Right);
                return Results.Ok(new { kind = "json", json = jsonDiff });
            }
            catch (System.Text.Json.JsonException)
            {
                var textDiff = TextDiffer.Diff(body.Left, body.Right);
                return Results.Ok(new { kind = "text", text = textDiff });
            }
        });
    }
}
