using AiLab.Core.Execution;
using AiLab.Core.Statistics;
using AiLab.Infrastructure.ModelCatalog;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class BenchmarkEndpoints
{
    public record BenchmarkRequestBody(int Count);

    public record BenchmarkResponse(IReadOnlyList<ExecutionRun> Runs, BenchmarkResult Stats);

    public static void MapBenchmarkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/requests/{id:guid}/benchmark", async (
            Guid id,
            BenchmarkRequestBody body,
            AiLabDbContext db,
            AiRequestExecutor executor,
            IProviderModelCatalogService catalog,
            CancellationToken ct) =>
        {
            if (body.Count is < 1 or > 100)
            {
                return Results.BadRequest(new { error = "Count must be between 1 and 100." });
            }

            var request = await db.Requests.FindAsync([id], ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            var variables = await db.WorkspaceVariables
                .Where(v => v.WorkspaceId == request.WorkspaceId)
                .ToDictionaryAsync(v => v.Name, v => v.Value, ct);
            var bindingContext = new BindingResolutionContext { WorkspaceVariables = variables };

            var models = await catalog.GetCatalogAsync(ct);
            var model = models.FirstOrDefault(m => m.ProviderId == request.ProviderId && m.ModelId == request.ModelId);

            var runs = new List<ExecutionRun>();
            for (var i = 0; i < body.Count; i++)
            {
                var run = await executor.ExecuteAsync(request, bindingContext, model, progress: null, ct);
                runs.Add(run);
                db.ExecutionRuns.Add(run);
                // Uncancellable: a Stop mid-benchmark still leaves the runs-so-far (including the
                // just-Canceled one) as real persisted history, not silently dropped.
                await db.SaveChangesAsync(CancellationToken.None);

                if (run.Status == ExecutionStatus.Canceled)
                {
                    break;
                }
            }

            var stats = BenchmarkAggregator.ComputeBenchmark(runs);
            return Results.Ok(new BenchmarkResponse(runs, stats));
        });
    }
}
