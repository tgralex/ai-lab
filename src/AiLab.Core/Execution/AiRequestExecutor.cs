using System.Text;
using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Core.Requests;
using AiLab.Core.Statistics;

namespace AiLab.Core.Execution;

/// <summary>
/// Orchestrates a single AI request: resolves {{...}} bindings, inlines attached-file text,
/// selects the right IAiProvider by AiRequestDefinition.ProviderId, and turns the provider's
/// streamed events + final result into a fully-populated ExecutionRun with a reproducible
/// snapshot. Takes no dependency on EF Core or ASP.NET Core — callers (the API layer) gather
/// BindingResolutionContext and persist the result.
/// </summary>
public sealed class AiRequestExecutor(
    IEnumerable<IAiProvider> providers,
    ICostCalculator costCalculator,
    IAttachmentContentProvider attachmentContentProvider)
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

        var (cachedText, cachedHashes) = await AppendAttachmentsAsync(cachedResolution.ResolvedText, request.CachedContext.AttachmentIds, cancellationToken);
        var (userText, userHashes) = await AppendAttachmentsAsync(userResolution.ResolvedText, request.UserContext.AttachmentIds, cancellationToken);
        var attachmentHashes = cachedHashes.Concat(userHashes).Distinct().ToList();

        var snapshot = new RequestSnapshot
        {
            ProviderId = request.ProviderId,
            ModelId = request.ModelId,
            SystemPrompt = request.SystemPrompt,
            ResolvedCachedContext = cachedText,
            ResolvedUserContext = userText,
            ResolvedBindings = resolvedBindings,
            AttachmentHashes = attachmentHashes,
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
            CachedContextText = cachedText,
            UserContextText = userText,
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

    /// <summary>Appends each attached file's extracted text after the resolved prompt text, returning the combined text plus the hashes actually used (for the snapshot).</summary>
    private async Task<(string Text, List<string> Hashes)> AppendAttachmentsAsync(string resolvedText, IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        if (attachmentIds.Count == 0)
        {
            return (resolvedText, []);
        }

        var builder = new StringBuilder(resolvedText);
        var hashes = new List<string>();

        foreach (var id in attachmentIds)
        {
            var content = await attachmentContentProvider.GetContentAsync(id, ct);
            if (content is null)
            {
                continue;
            }

            hashes.Add(content.Sha256);

            if (!string.IsNullOrEmpty(content.ExtractedText))
            {
                builder.Append("\n\n--- Attached file: ").Append(content.Filename).Append(" ---\n");
                builder.Append(content.ExtractedText);
            }
        }

        return (builder.ToString(), hashes);
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
