using System.Text;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Core.Requests;
using AiLab.Core.Statistics;

namespace AiLab.Core.Execution;

/// <summary>
/// Orchestrates a single AI request: resolves {{...}} bindings, selects the right IAiProvider by
/// AiRequestDefinition.ProviderId, and turns the provider's streamed events + final result into a
/// fully-populated ExecutionRun with a reproducible snapshot. Takes no dependency on EF Core or
/// ASP.NET Core — callers (the API layer) gather BindingResolutionContext and persist the result.
/// </summary>
public sealed class AiRequestExecutor(IEnumerable<IAiProvider> providers, ICostCalculator costCalculator)
{
    public async Task<ExecutionRun> ExecuteAsync(
        AiRequestDefinition request,
        BindingResolutionContext bindingContext,
        ProviderModel? modelForCost,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        var provider = providers.FirstOrDefault(p => p.Id == request.ProviderId);

        var systemResolution = InputBindingResolver.Resolve(request.SystemPrompt, bindingContext);
        var cachedResolution = InputBindingResolver.Resolve(request.CachedContext.Text, bindingContext);
        var userResolution = InputBindingResolver.Resolve(request.UserContext.Text, bindingContext);

        var resolvedBindings = MergeBindings(systemResolution, cachedResolution, userResolution);

        var snapshot = new RequestSnapshot
        {
            ProviderId = request.ProviderId,
            ModelId = request.ModelId,
            SystemPrompt = request.SystemPrompt,
            ResolvedCachedContext = cachedResolution.ResolvedText,
            ResolvedUserContext = userResolution.ResolvedText,
            ResolvedBindings = resolvedBindings,
            AttachmentHashes = [], // attachment content inlining is not wired into provider calls yet (see plan's deferred items)
            ReasoningEffort = request.Reasoning?.Effort,
            Streaming = request.StreamingEnabled,
            MaxOutputTokens = request.MaxOutputTokens,
            StructuredOutputSchema = request.StructuredOutputSchema,
            PromptCacheKey = request.PromptCacheKey,
            ProviderSettings = request.ProviderSettings,
            CapturedAt = DateTimeOffset.UtcNow,
        };

        var run = new ExecutionRun
        {
            AiRequestId = request.Id,
            ProviderId = request.ProviderId,
            RequestedModel = request.ModelId,
            Snapshot = snapshot,
        };

        if (provider is null)
        {
            run.Status = ExecutionStatus.Failed;
            run.StartedAt = DateTimeOffset.UtcNow;
            run.FinishedAt = run.StartedAt;
            run.Failure = new FailureInfo { Message = $"No provider registered for id '{request.ProviderId}'." };
            return run;
        }

        var executionContext = new AiRequestExecutionContext
        {
            ModelId = request.ModelId,
            SystemPrompt = systemResolution.ResolvedText,
            CachedContextText = cachedResolution.ResolvedText,
            UserContextText = userResolution.ResolvedText,
            Attachments = [],
            Streaming = request.StreamingEnabled,
            MaxOutputTokens = request.MaxOutputTokens,
            ReasoningEffort = request.Reasoning?.Effort,
            StructuredOutputSchema = request.StructuredOutputSchema,
            PromptCacheKey = request.PromptCacheKey,
            ProviderSettings = request.ProviderSettings,
        };

        var trackingProgress = new TrackingProgress(run, progress);

        try
        {
            var result = await provider.ExecuteAsync(executionContext, trackingProgress, cancellationToken);
            ApplyResult(run, result, modelForCost);
        }
        catch (OperationCanceledException)
        {
            run.Status = ExecutionStatus.Canceled;
            run.FinishedAt = DateTimeOffset.UtcNow;
        }

        return run;
    }

    private void ApplyResult(ExecutionRun run, ProviderExecutionResult result, ProviderModel? modelForCost)
    {
        run.FinishedAt ??= DateTimeOffset.UtcNow;
        run.Usage = result.Usage;
        run.ResponseId = result.ResponseId;
        run.ActualModel = result.ActualModel;
        run.FinishReason = result.FinishReason;
        run.RawProviderResponseJson = result.RawResponseJson;
        run.NormalizedProviderRequestJson = result.NormalizedRequestJson;
        run.RequestSizeBytes = result.NormalizedRequestJson is null ? null : Encoding.UTF8.GetByteCount(result.NormalizedRequestJson);
        run.ResponseSizeBytes = result.RawResponseJson is null ? null : Encoding.UTF8.GetByteCount(result.RawResponseJson);

        if (result.Success)
        {
            run.Status = ExecutionStatus.Completed;
            run.Output = result.OutputText;

            if (modelForCost is not null)
            {
                var cost = costCalculator.Calculate(result.Usage, modelForCost);
                run.EstimatedInputCost = cost.InputCost;
                run.EstimatedCachedInputCost = cost.CachedInputCost;
                run.EstimatedOutputCost = cost.OutputCost;
                run.EstimatedTotalCost = cost.TotalCost;
            }
        }
        else
        {
            run.Status = ExecutionStatus.Failed;
            run.Failure = result.Failure;
            if (run.Failure is not null)
            {
                run.RetryCount = run.Failure.RetryCount;
            }
        }
    }

    private static Dictionary<string, string> MergeBindings(params BindingResolutionResult[] resolutions)
    {
        var merged = new Dictionary<string, string>();
        foreach (var resolution in resolutions)
        {
            foreach (var (key, value) in resolution.ResolvedBindings)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>Timestamps StartedAt / FirstResponseEventAt / FirstOutputTokenAt off event arrival, then forwards to the caller's own progress (e.g. an SSE writer).</summary>
    private sealed class TrackingProgress(ExecutionRun run, IProgress<AiStreamEvent>? inner) : IProgress<AiStreamEvent>
    {
        public void Report(AiStreamEvent value)
        {
            switch (value.Kind)
            {
                case AiStreamEventKind.Started:
                    run.StartedAt ??= value.Timestamp;
                    run.Status = ExecutionStatus.Running;
                    break;

                case AiStreamEventKind.FirstProtocolEvent:
                    run.FirstResponseEventAt ??= value.Timestamp;
                    run.Status = ExecutionStatus.Streaming;
                    break;

                case AiStreamEventKind.OutputTextDelta:
                    run.FirstOutputTokenAt ??= value.Timestamp;
                    break;

                case AiStreamEventKind.Completed:
                    run.FinishedAt ??= value.Timestamp;
                    break;
            }

            inner?.Report(value);
        }
    }
}
