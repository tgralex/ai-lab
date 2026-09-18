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

    /// <summary>Id is null for a node the client just added this edit session (server mints one);
    /// non-null for an existing node being round-tripped so its identity — and any history/bindings
    /// keyed on it — stays stable across saves.</summary>
    public record PlanRequestDto(Guid? Id, Guid AiRequestId, string? Label, bool IsFinalOutput);

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
            plan.Requests.AddRange(body.Requests.Select(r => new ExecutionPlanRequest
            {
                Id = r.Id ?? Guid.NewGuid(),
                AiRequestId = r.AiRequestId,
                Label = r.Label,
                IsFinalOutput = r.IsFinalOutput,
            }));
            plan.Dependencies.AddRange(body.Dependencies.Select(d => new ExecutionPlanDependency { FromNodeId = d.From, ToNodeId = d.To }));

            var requestsById = await LoadRequestsByIdAsync(plan, db, ct);
            var validation = ExecutionPlanGraph.Validate(plan, requestsById);
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

            var requestsById = await LoadRequestsByIdAsync(plan, db, ct);
            var validation = ExecutionPlanGraph.Validate(plan, requestsById);
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

            // Reconcile in place rather than Clear()+AddRange(): a node the client round-trips keeps
            // its existing Id (needed for stable identity — see PlanRequestDto), and EF's change
            // tracker treats a delete-then-reinsert of that same key as an update racing a delete,
            // throwing DbUpdateConcurrencyException. Only genuinely removed/added nodes are removed/added.
            var incomingById = body.Requests.Where(r => r.Id.HasValue).ToDictionary(r => r.Id!.Value);
            plan.Requests.RemoveAll(existing => !incomingById.ContainsKey(existing.Id));
            var existingIds = plan.Requests.Select(existing => existing.Id).ToHashSet();
            foreach (var dto in body.Requests)
            {
                if (dto.Id.HasValue && existingIds.Contains(dto.Id.Value))
                {
                    var node = plan.Requests.First(existing => existing.Id == dto.Id.Value);
                    node.Label = dto.Label;
                    node.IsFinalOutput = dto.IsFinalOutput;
                }
                else
                {
                    plan.Requests.Add(new ExecutionPlanRequest
                    {
                        Id = dto.Id ?? Guid.NewGuid(),
                        AiRequestId = dto.AiRequestId,
                        Label = dto.Label,
                        IsFinalOutput = dto.IsFinalOutput,
                    });
                }
            }

            plan.Dependencies.Clear();
            plan.Dependencies.AddRange(body.Dependencies.Select(d => new ExecutionPlanDependency { FromNodeId = d.From, ToNodeId = d.To }));

            var requestsById = await LoadRequestsByIdAsync(plan, db, ct);
            var validation = ExecutionPlanGraph.Validate(plan, requestsById);
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

            // Uncancellable: a Stop mid-plan still leaves the plan run and its participating
            // ExecutionRuns (including any Canceled ones) as real persisted history.
            db.ExecutionRuns.AddRange(completedRuns);
            db.ExecutionPlanRuns.Add(planRun);
            await db.SaveChangesAsync(CancellationToken.None);
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

            // Not tied to `ct`: the engine itself already reacts to `ct` internally per-node
            // (recording Canceled runs rather than throwing) — the Task.Run wrapper shouldn't preempt that.
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
            }, CancellationToken.None);

            try
            {
                await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                {
                    await sseWriter.WriteAsync("plan-event", evt, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected (Stop button) — fall through to still persist what completed below.
            }

            var planRun = await executeTask;
            db.ExecutionRuns.AddRange(completedRuns);
            db.ExecutionPlanRuns.Add(planRun);
            await db.SaveChangesAsync(CancellationToken.None);

            if (!ct.IsCancellationRequested)
            {
                await sseWriter.WriteAsync("plan-completed", planRun, ct);
            }
        });
    }

    private static async Task<Dictionary<Guid, Core.Requests.AiRequestDefinition>> LoadRequestsByIdAsync(Core.ExecutionPlans.ExecutionPlan plan, AiLabDbContext db, CancellationToken ct)
    {
        var requestIds = plan.Requests.Select(r => r.AiRequestId).Distinct().ToList();
        var requests = await db.Requests.Where(r => requestIds.Contains(r.Id)).ToListAsync(ct);
        return requests.ToDictionary(r => r.Id);
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

        var requestsById = await LoadRequestsByIdAsync(plan, db, ct);

        var validation = ExecutionPlanGraph.Validate(plan, requestsById);
        if (!validation.IsValid)
        {
            return (null, null, null, null, Results.BadRequest(new { errors = validation.Errors }));
        }

        var variables = await db.WorkspaceVariables
            .Where(v => v.WorkspaceId == plan.WorkspaceId)
            .ToDictionaryAsync(v => v.Name, v => v.Value, ct);
        var bindingContext = new BindingResolutionContext { WorkspaceVariables = variables };

        var catalogModels = await catalog.GetCatalogAsync(ct);
        var modelsForCost = requestsById.Values.ToDictionary(
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
