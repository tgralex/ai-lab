using System.Threading.Channels;
using AiLab.Api.Sse;
using AiLab.Core.Execution;
using AiLab.Core.Providers;
using AiLab.Infrastructure.ModelCatalog;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class ExecutionEndpoints
{
    public static void MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/requests/{id:guid}/execute", async (
            Guid id,
            AiLabDbContext db,
            AiRequestExecutor executor,
            IProviderModelCatalogService catalog,
            CancellationToken ct) =>
        {
            var request = await db.Requests.FindAsync([id], ct);
            if (request is null)
            {
                return Results.NotFound();
            }

            var bindingContext = await BuildBindingContextAsync(request.WorkspaceId, db, ct);
            var model = await FindModelForCostAsync(request.ProviderId, request.ModelId, catalog, ct);
            var run = await executor.ExecuteAsync(request, bindingContext, model, progress: null, ct);

            db.ExecutionRuns.Add(run);
            await db.SaveChangesAsync(ct);

            return Results.Ok(run);
        });

        app.MapGet("/api/requests/{id:guid}/execute-stream", async (
            Guid id,
            HttpResponse httpResponse,
            AiLabDbContext db,
            AiRequestExecutor executor,
            IProviderModelCatalogService catalog,
            CancellationToken ct) =>
        {
            var request = await db.Requests.FindAsync([id], ct);
            if (request is null)
            {
                httpResponse.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var bindingContext = await BuildBindingContextAsync(request.WorkspaceId, db, ct);
            var model = await FindModelForCostAsync(request.ProviderId, request.ModelId, catalog, ct);

            var sseWriter = new SseResponseWriter(httpResponse);
            sseWriter.PrepareResponse();

            var channel = Channel.CreateUnbounded<AiStreamEvent>();
            var progress = new ChannelProgress(channel.Writer);

            var executeTask = Task.Run(async () =>
            {
                try
                {
                    return await executor.ExecuteAsync(request, bindingContext, model, progress, ct);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            await foreach (var streamEvent in channel.Reader.ReadAllAsync(ct))
            {
                await sseWriter.WriteAsync("stream-event", streamEvent, ct);
            }

            var run = await executeTask;
            db.ExecutionRuns.Add(run);
            await db.SaveChangesAsync(ct);

            await sseWriter.WriteAsync("run-completed", run, ct);
        });
    }

    private static async Task<BindingResolutionContext> BuildBindingContextAsync(Guid workspaceId, AiLabDbContext db, CancellationToken ct)
    {
        var variables = await db.WorkspaceVariables
            .Where(v => v.WorkspaceId == workspaceId)
            .ToDictionaryAsync(v => v.Name, v => v.Value, ct);

        return new BindingResolutionContext { WorkspaceVariables = variables };
    }

    /// <summary>Best-effort: cost estimation is skipped (not blocked) if the catalog has no pricing for this model yet.</summary>
    private static async Task<Core.Models.ProviderModel?> FindModelForCostAsync(string providerId, string modelId, IProviderModelCatalogService catalog, CancellationToken ct)
    {
        var models = await catalog.GetCatalogAsync(ct);
        return models.FirstOrDefault(m => m.ProviderId == providerId && m.ModelId == modelId);
    }

    private sealed class ChannelProgress(ChannelWriter<AiStreamEvent> writer) : IProgress<AiStreamEvent>
    {
        public void Report(AiStreamEvent value) => writer.TryWrite(value);
    }
}
