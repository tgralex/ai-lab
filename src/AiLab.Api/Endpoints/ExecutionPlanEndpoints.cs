using System.Threading.Channels;
using AiLab.Api.Sse;
using AiLab.Core.Execution;
using AiLab.Core.ExecutionPlans;
using AiLab.Core.Models;
using AiLab.Infrastructure.ModelCatalog;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Api.Endpoints;

public static class ExecutionPlanEndpoints
{
    public record DependencyDto(Guid From, Guid To);

    public record PlanRequestDto(Guid AiRequestId, bool IsFinalOutput);

    public record CreatePlanBody(string Name, List<PlanRequestDto> Requests, List<DependencyDto> Dependencies);

    public record ExecuteOptionsBody(int? GlobalMaxConcurrency, Dictionary<string, int>? ProviderMaxConcurrency);

    public record PlanDetailResponse(ExecutionPlan Plan, IReadOnlyList<ExecutionLevel> Levels, ExecutionPlanValidationResult Validation);

    public static void MapExecutionPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/execution-plans");

        app.MapGet("/api/workspaces/{workspaceId:guid}/execution-plans", async (Guid workspaceId, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ExecutionPlans.Where(p => p.WorkspaceId == workspaceId).ToListAsync(ct)));

        app.MapPost("/api/workspaces/{workspaceId:guid}/execution-plans", async (Guid workspaceId, CreatePlanBody body, AiLabDbContext db, CancellationToken ct) =>
        {
            var plan = new ExecutionPlan { WorkspaceId = workspaceId, Name = body.Name };
            plan.Requests.AddRange(body.Requests.Select(r => new ExecutionPlanRequest { AiRequestId = r.AiRequestId, IsFinalOutput = r.IsFinalOutput }));
            plan.Dependencies.AddRange(body.Dependencies.Select(d => new ExecutionPlanDependency { FromRequestId = d.From, ToRequestId = d.To }));

            var validation = ExecutionPlanGraph.Validate(plan);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { errors = validation.Errors });
            }

            db.ExecutionPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/execution-plans/{plan.Id}", plan);
        });

        group.MapGet("/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var plan = await db.ExecutionPlans.FindAsync([id], ct);
            if (plan is null)
            {
                return Results.NotFound();
            }

            var validation = ExecutionPlanGraph.Validate(plan);
            var levels = validation.IsValid ? ExecutionPlanGraph.DeriveLevels(plan) : [];
            return Results.Ok(new PlanDetailResponse(plan, levels, validation));
        });

        group.MapPut("/{id:guid}", async (Guid id, CreatePlanBody body, AiLabDbContext db, CancellationToken ct) =>
        {
            var plan = await db.ExecutionPlans.FindAsync([id], ct);
            if (plan is null)
            {
                return Results.NotFound();
            }

            plan.Name = body.Name;
            plan.Requests.Clear();
            plan.Requests.AddRange(body.Requests.Select(r => new ExecutionPlanRequest { AiRequestId = r.AiRequestId, IsFinalOutput = r.IsFinalOutput }));
            plan.Dependencies.Clear();
            plan.Dependencies.AddRange(body.Dependencies.Select(d => new ExecutionPlanDependency { FromRequestId = d.From, ToRequestId = d.To }));

            var validation = ExecutionPlanGraph.Validate(plan);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new { errors = validation.Errors });
            }

            plan.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(plan);
        });

        group.MapDelete("/{id:guid}", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
        {
            var plan = await db.ExecutionPlans.FindAsync([id], ct);
            if (plan is null)
            {
                return Results.NotFound();
            }

            db.ExecutionPlans.Remove(plan);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/runs", async (Guid id, AiLabDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ExecutionPlanRuns.Where(r => r.ExecutionPlanId == id).OrderByDescending(r => r.StartedAt).ToListAsync(ct)));

        group.MapPost("/{id:guid}/execute", async (
            Guid id,
            ExecuteOptionsBody? body,
            AiLabDbContext db,
            ExecutionPlanEngine engine,
            IProviderModelCatalogService catalog,
            CancellationToken ct) =>
        {
            var (plan, requestsById, bindingContext, modelsForCost, error) = await PrepareExecutionAsync(id, db, catalog, ct);
            if (error is not null)
            {
                return error;
            }

            var options = BuildOptions(body);
            var completedRuns = new List<ExecutionRun>();
            var collector = new ChannelProgress(null, run => completedRuns.Add(run));
            var planRun = await engine.ExecuteAsync(plan!, requestsById!, bindingContext!, modelsForCost!, options, collector, ct);

            db.ExecutionRuns.AddRange(completedRuns);
            db.ExecutionPlanRuns.Add(planRun);
            await db.SaveChangesAsync(ct);
            return Results.Ok(planRun);
        });

        group.MapGet("/{id:guid}/execute-stream", async (
            Guid id,
            HttpResponse httpResponse,
            AiLabDbContext db,
            ExecutionPlanEngine engine,
            IProviderModelCatalogService catalog,
            CancellationToken ct) =>
        {
            var (plan, requestsById, bindingContext, modelsForCost, error) = await PrepareExecutionAsync(id, db, catalog, ct);
            if (error is not null)
            {
                httpResponse.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var sseWriter = new SseResponseWriter(httpResponse);
            sseWriter.PrepareResponse();

            var channel = Channel.CreateUnbounded<PlanExecutionEvent>();
            var completedRuns = new List<ExecutionRun>();
            var progress = new ChannelProgress(channel.Writer, run => completedRuns.Add(run));

            var executeTask = Task.Run(async () =>
            {
                try
                {
                    return await engine.ExecuteAsync(plan!, requestsById!, bindingContext!, modelsForCost!, new PlanExecutionOptions(), progress, ct);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct);

            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                await sseWriter.WriteAsync("plan-event", evt, ct);
            }

            var planRun = await executeTask;
            db.ExecutionRuns.AddRange(completedRuns);
            db.ExecutionPlanRuns.Add(planRun);
            await db.SaveChangesAsync(ct);
            await sseWriter.WriteAsync("plan-completed", planRun, ct);
        });
    }

    private static async Task<(
        Core.ExecutionPlans.ExecutionPlan? Plan,
        Dictionary<Guid, Core.Requests.AiRequestDefinition>? RequestsById,
        BindingResolutionContext? BindingContext,
        Dictionary<Guid, ProviderModel?>? ModelsForCost,
        IResult? Error)> PrepareExecutionAsync(Guid planId, AiLabDbContext db, IProviderModelCatalogService catalog, CancellationToken ct)
    {
        var plan = await db.ExecutionPlans.FindAsync([planId], ct);
        if (plan is null)
        {
            return (null, null, null, null, Results.NotFound());
        }

        var validation = ExecutionPlanGraph.Validate(plan);
        if (!validation.IsValid)
        {
            return (null, null, null, null, Results.BadRequest(new { errors = validation.Errors }));
        }

        var requestIds = plan.Requests.Select(r => r.AiRequestId).ToList();
        var requests = await db.Requests.Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
        var requestsById = requests.ToDictionary(r => r.Id);

        var variables = await db.WorkspaceVariables
            .Where(v => v.WorkspaceId == plan.WorkspaceId)
            .ToDictionaryAsync(v => v.Name, v => v.Value, ct);
        var bindingContext = new BindingResolutionContext { WorkspaceVariables = variables };

        var catalogModels = await catalog.GetCatalogAsync(ct);
        var modelsForCost = requests.ToDictionary(
            r => r.Id,
            r => catalogModels.FirstOrDefault(m => m.ProviderId == r.ProviderId && m.ModelId == r.ModelId));

        return (plan, requestsById, bindingContext, modelsForCost, null);
    }

    private static PlanExecutionOptions BuildOptions(ExecuteOptionsBody? body) => new()
    {
        GlobalMaxConcurrency = body?.GlobalMaxConcurrency ?? 8,
        ProviderMaxConcurrency = body?.ProviderMaxConcurrency ?? new Dictionary<string, int>(),
    };

    private sealed class ChannelProgress(ChannelWriter<PlanExecutionEvent>? writer, Action<ExecutionRun>? onNodeCompleted = null) : IProgress<PlanExecutionEvent>
    {
        public void Report(PlanExecutionEvent value)
        {
            writer?.TryWrite(value);
            if (value is { Kind: PlanExecutionEventKind.NodeCompleted, Run: { } run })
            {
                onNodeCompleted?.Invoke(run);
            }
        }
    }
}
