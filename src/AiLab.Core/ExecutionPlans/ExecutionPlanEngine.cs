using System.Text.Json;
using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Requests;

namespace AiLab.Core.ExecutionPlans;

/// <summary>
/// Runs an ExecutionPlan's DAG: each node awaits only its own declared dependencies (not a strict
/// level-by-level barrier), so unrelated late finishers in the same derived level never block a
/// downstream request whose actual deps are already done. Concurrency is gated by two SemaphoreSlims
/// per node — one global, one per-provider — acquired before the node's AiRequestExecutor call.
/// Levels from ExecutionPlanGraph.DeriveLevels are used only for *reporting* (group stats).
/// </summary>
public sealed class ExecutionPlanEngine(AiRequestExecutor requestExecutor)
{
    public async Task<ExecutionPlanRun> ExecuteAsync(
        ExecutionPlan plan,
        IReadOnlyDictionary<Guid, AiRequestDefinition> requestsById,
        BindingResolutionContext baseBindingContext,
        IReadOnlyDictionary<Guid, ProviderModel?> modelsForCost,
        PlanExecutionOptions options,
        IProgress<PlanExecutionEvent>? progress,
        CancellationToken cancellationToken)
    {
        var validation = ExecutionPlanGraph.Validate(plan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Execution plan is invalid: {string.Join("; ", validation.Errors)}");
        }

        var levels = ExecutionPlanGraph.DeriveLevels(plan);
        var planRun = new ExecutionPlanRun
        {
            ExecutionPlanId = plan.Id,
            Status = ExecutionStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            PlanGraphSnapshotJson = JsonSerializer.Serialize(new
            {
                Requests = plan.Requests.Select(r => new { r.AiRequestId, r.IsFinalOutput }),
                Dependencies = plan.Dependencies.Select(d => new { d.FromRequestId, d.ToRequestId }),
            }),
        };

        var dependenciesByNode = plan.Requests.ToDictionary(
            r => r.AiRequestId,
            r => plan.Dependencies.Where(d => d.ToRequestId == r.AiRequestId).Select(d => d.FromRequestId).ToList());

        var nodeCompletions = plan.Requests.ToDictionary(r => r.AiRequestId, _ => new TaskCompletionSource<ExecutionRun>());
        using var planCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var globalSemaphore = new SemaphoreSlim(options.GlobalMaxConcurrency);
        var providerSemaphores = new Dictionary<string, SemaphoreSlim>();

        SemaphoreSlim GetProviderSemaphore(string providerId)
        {
            lock (providerSemaphores)
            {
                if (!providerSemaphores.TryGetValue(providerId, out var sem))
                {
                    var cap = options.ProviderMaxConcurrency.GetValueOrDefault(providerId, options.GlobalMaxConcurrency);
                    sem = new SemaphoreSlim(cap);
                    providerSemaphores[providerId] = sem;
                }

                return sem;
            }
        }

        var nodeTasks = plan.Requests.Select(node => RunNodeAsync(
            node,
            requestsById[node.AiRequestId],
            dependenciesByNode[node.AiRequestId],
            nodeCompletions,
            requestsById,
            baseBindingContext,
            modelsForCost.GetValueOrDefault(node.AiRequestId),
            globalSemaphore,
            GetProviderSemaphore,
            progress,
            planCts)).ToList();

        var completedRuns = await Task.WhenAll(nodeTasks);

        planRun.FinishedAt = DateTimeOffset.UtcNow;
        planRun.ExecutionRunIds = completedRuns.Select(r => r.Id).ToList();
        planRun.Status = completedRuns.Any(r => r.Status == ExecutionStatus.Canceled) && cancellationToken.IsCancellationRequested
            ? ExecutionStatus.Canceled
            : completedRuns.All(r => r.Status == ExecutionStatus.Completed) ? ExecutionStatus.Completed : ExecutionStatus.Failed;

        planRun.CumulativeRequestDuration = TimeSpan.FromMilliseconds(completedRuns.Where(r => r.TotalDuration.HasValue).Sum(r => r.TotalDuration!.Value.TotalMilliseconds));
        planRun.TotalInputTokens = completedRuns.Sum(r => r.Usage.InputTokens);
        planRun.TotalCachedInputTokens = completedRuns.Sum(r => r.Usage.CachedInputTokens);
        planRun.TotalOutputTokens = completedRuns.Sum(r => r.Usage.OutputTokens);
        planRun.TotalReasoningTokens = completedRuns.Sum(r => r.Usage.ReasoningTokens);
        planRun.TotalEstimatedCost = completedRuns.Sum(r => r.EstimatedTotalCost ?? 0);

        var runsById = completedRuns.ToDictionary(r => r.AiRequestId);
        planRun.Groups = levels.Select(level =>
        {
            var levelRuns = level.RequestIds.Select(id => runsById[id]).ToList();
            var starts = levelRuns.Where(r => r.StartedAt.HasValue).Select(r => r.StartedAt!.Value).ToList();
            var finishes = levelRuns.Where(r => r.FinishedAt.HasValue).Select(r => r.FinishedAt!.Value).ToList();

            return new ExecutionGroupRun
            {
                LevelIndex = level.Index,
                AiRequestIds = level.RequestIds,
                ExecutionRunIds = levelRuns.Select(r => r.Id).ToList(),
                WallClockDuration = starts.Count > 0 && finishes.Count > 0 ? finishes.Max() - starts.Min() : TimeSpan.Zero,
                CumulativeRequestDuration = TimeSpan.FromMilliseconds(levelRuns.Where(r => r.TotalDuration.HasValue).Sum(r => r.TotalDuration!.Value.TotalMilliseconds)),
            };
        }).ToList();

        progress?.Report(PlanExecutionEvent.PlanCompleted());
        return planRun;
    }

    private async Task<ExecutionRun> RunNodeAsync(
        ExecutionPlanRequest node,
        AiRequestDefinition request,
        IReadOnlyList<Guid> dependencyIds,
        Dictionary<Guid, TaskCompletionSource<ExecutionRun>> nodeCompletions,
        IReadOnlyDictionary<Guid, AiRequestDefinition> requestsById,
        BindingResolutionContext baseBindingContext,
        ProviderModel? modelForCost,
        SemaphoreSlim globalSemaphore,
        Func<string, SemaphoreSlim> getProviderSemaphore,
        IProgress<PlanExecutionEvent>? progress,
        CancellationTokenSource planCts)
    {
        ExecutionRun run;
        try
        {
            var upstreamRuns = await Task.WhenAll(dependencyIds.Select(id => nodeCompletions[id].Task));
            var bindingContext = BuildBindingContext(baseBindingContext, dependencyIds, upstreamRuns, requestsById);

            await globalSemaphore.WaitAsync(planCts.Token);
            try
            {
                var providerSemaphore = getProviderSemaphore(request.ProviderId);
                await providerSemaphore.WaitAsync(planCts.Token);
                try
                {
                    progress?.Report(PlanExecutionEvent.NodeStarted(node.AiRequestId));
                    run = await ExecuteWithFailurePolicyAsync(request, bindingContext, modelForCost, planCts);
                }
                finally
                {
                    providerSemaphore.Release();
                }
            }
            finally
            {
                globalSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            run = BuildCanceledRun(request);
        }

        progress?.Report(PlanExecutionEvent.NodeCompleted(node.AiRequestId, run));
        nodeCompletions[node.AiRequestId].TrySetResult(run);
        return run;
    }

    private async Task<ExecutionRun> ExecuteWithFailurePolicyAsync(
        AiRequestDefinition request,
        BindingResolutionContext bindingContext,
        ProviderModel? modelForCost,
        CancellationTokenSource planCts)
    {
        var attempt = 0;
        var maxAttempts = request.FailurePolicy == FailurePolicy.Retry ? Math.Max(1, request.RetryPolicy.MaxRetries + 1) : 1;
        ExecutionRun run;

        while (true)
        {
            attempt++;
            run = await requestExecutor.ExecuteAsync(request, bindingContext, modelForCost, progress: null, planCts.Token);
            run.RetryCount += attempt - 1;

            if (run.Status != ExecutionStatus.Failed || attempt >= maxAttempts)
            {
                break;
            }

            var delayMs = request.RetryPolicy.BaseBackoffMs * (1 << (attempt - 1));
            await Task.Delay(delayMs, planCts.Token);
        }

        if (run.Status == ExecutionStatus.Failed && request.FailurePolicy == FailurePolicy.FailPlan)
        {
            planCts.Cancel();
        }

        // Retry and ContinueWithError both let the plan continue with this node's (possibly failed) result.
        return run;
    }

    private static BindingResolutionContext BuildBindingContext(
        BindingResolutionContext baseContext,
        IReadOnlyList<Guid> dependencyIds,
        IReadOnlyList<ExecutionRun> upstreamRuns,
        IReadOnlyDictionary<Guid, AiRequestDefinition> requestsById)
    {
        var priorOutputs = new Dictionary<string, PriorRequestOutput>();
        for (var i = 0; i < dependencyIds.Count; i++)
        {
            var depRequest = requestsById[dependencyIds[i]];
            var depRun = upstreamRuns[i];
            var outputText = depRun.Output ?? string.Empty;

            JsonElement? parsedJson = null;
            try
            {
                parsedJson = JsonDocument.Parse(outputText).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Output isn't JSON — {{X.json.path}} bindings against it correctly stay unresolved.
            }

            priorOutputs[depRequest.Name] = new PriorRequestOutput { RawOutputText = outputText, ParsedJson = parsedJson };
        }

        return new BindingResolutionContext { WorkspaceVariables = baseContext.WorkspaceVariables, PriorRequestOutputs = priorOutputs };
    }

    private static ExecutionRun BuildCanceledRun(AiRequestDefinition request) => new()
    {
        AiRequestId = request.Id,
        ProviderId = request.ProviderId,
        RequestedModel = request.ModelId,
        Status = ExecutionStatus.Canceled,
        StartedAt = DateTimeOffset.UtcNow,
        FinishedAt = DateTimeOffset.UtcNow,
        Snapshot = new RequestSnapshot
        {
            ProviderId = request.ProviderId,
            ModelId = request.ModelId,
            CapturedAt = DateTimeOffset.UtcNow,
        },
    };
}
