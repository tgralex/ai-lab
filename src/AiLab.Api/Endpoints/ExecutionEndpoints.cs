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
    /// <summary>Optional per-call model override for multi-model comparison runs — never persisted onto the saved request.</summary>
    public record ExecuteOverrideBody(string? ProviderId, string? ModelId);

    public record ArchiveRunBody(bool IsArchived);

    public static void MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/requests/{id:guid}/execute", async (
            Guid id,
            ExecuteOverrideBody? body,
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

            var providerId = body?.ProviderId ?? request.ProviderId;
            var modelId = body?.ModelId ?? request.ModelId;
            var executionRequest = body?.ProviderId is null && body?.ModelId is null
                ? request
                : request.CloneForModel(providerId, modelId);

            var bindingContext = await BuildBindingContextAsync(request.WorkspaceId, db, ct);
            var model = await FindModelForCostAsync(providerId, modelId, catalog, ct);
            var run = await executor.ExecuteAsync(executionRequest, bindingContext, model, progress: null, ct);

            // A Stopped/disconnected request still leaves a real Canceled run behind — persist with
            // an uncancellable token so that record isn't lost to the same disconnect that made it.
            db.ExecutionRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);

            return Results.Ok(run);
        });

        app.MapGet("/api/requests/{id:guid}/execute-stream", async (
            Guid id,
            string? providerId,
            string? modelId,
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

            var effectiveProviderId = providerId ?? request.ProviderId;
            var effectiveModelId = modelId ?? request.ModelId;
            var executionRequest = providerId is null && modelId is null
                ? request
                : request.CloneForModel(effectiveProviderId, effectiveModelId);

            var bindingContext = await BuildBindingContextAsync(request.WorkspaceId, db, ct);
            var model = await FindModelForCostAsync(effectiveProviderId, effectiveModelId, catalog, ct);

            var sseWriter = new SseResponseWriter(httpResponse);
            sseWriter.PrepareResponse();

            var channel = Channel.CreateUnbounded<AiStreamEvent>();
            var progress = new ChannelProgress(channel.Writer);

            // Not tied to `ct`: the executor itself already reacts to `ct` internally (recording a
            // Canceled run rather than throwing) — the Task.Run wrapper shouldn't preempt that.
            var executeTask = Task.Run(async () =>
            {
                try
                {
                    return await executor.ExecuteAsync(executionRequest, bindingContext, model, progress, ct);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, CancellationToken.None);

            try
            {
                await foreach (var streamEvent in channel.Reader.ReadAllAsync(ct))
                {
                    await sseWriter.WriteAsync("stream-event", streamEvent, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected (Stop button) — fall through to still persist the Canceled run below.
            }

            var run = await executeTask;
            db.ExecutionRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);

            if (!ct.IsCancellationRequested)
            {
                await sseWriter.WriteAsync("run-completed", run, ct);
            }
        });

        app.MapGet("/api/runs/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var run = await db.ExecutionRuns.FindAsync([id], ct);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });

        app.MapPost("/api/runs/{id:guid}/archive", async (Guid id, ArchiveRunBody body, AiLabDbContext db, CancellationToken ct) =>
        {
            var run = await db.ExecutionRuns.FindAsync([id], ct);
            if (run is null)
            {
                return Results.NotFound();
            }

            run.IsArchived = body.IsArchived;
            await db.SaveChangesAsync(ct);
            return Results.Ok(run);
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
