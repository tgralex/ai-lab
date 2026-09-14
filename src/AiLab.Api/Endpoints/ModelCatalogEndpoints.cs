using AiLab.Core.Execution;
using AiLab.Core.Statistics;
using AiLab.Infrastructure.ModelCatalog;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class ModelCatalogEndpoints
{
    public static void MapModelCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/models");

        group.MapGet("/", async (IProviderModelCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetCatalogAsync(ct)));

        group.MapGet("/status", async (IProviderModelCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetStatusAsync(ct)));

        group.MapPost("/refresh", async (string? providerId, IProviderModelCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.RefreshAsync(providerId, ct)));

        // Observed (from our own run history) vs. the catalog's provider-declared metadata above —
        // computed on demand from ExecutionRuns, never persisted as a separate table that could drift.
        group.MapGet("/observed-stats", async (AiLabDbContext db, CancellationToken ct) =>
        {
            var completedRuns = await db.ExecutionRuns
                .Where(r => r.Status == ExecutionStatus.Completed)
                .ToListAsync(ct);

            var grouped = completedRuns
                .GroupBy(r => (r.ProviderId, ModelId: r.ActualModel ?? r.RequestedModel ?? "unknown"))
                .Select(g => ObservedModelStatistics.Compute(g.Key.ProviderId, g.Key.ModelId, g.ToList()))
                .ToList();

            return Results.Ok(grouped);
        });
    }
}
