using OpenAiBench.Core.Cost;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Ports;

namespace OpenAiBench.Core.Execution;

public sealed class ExperimentRunner : IExperimentRunner
{
    private readonly IOpenAiExperimentClient _client;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IReadOnlyList<IFileContentExtractor> _extractors;
    private readonly IClock _clock;
    private readonly ICostCalculator _costCalculator;
    private readonly IPricingProvider _pricingProvider;

    public ExperimentRunner(
        IOpenAiExperimentClient client,
        IWorkspaceStore workspaceStore,
        IEnumerable<IFileContentExtractor> extractors,
        IClock clock,
        ICostCalculator costCalculator,
        IPricingProvider pricingProvider)
    {
        _client = client;
        _workspaceStore = workspaceStore;
        _extractors = extractors.ToList();
        _clock = clock;
        _costCalculator = costCalculator;
        _pricingProvider = pricingProvider;
    }

    public async Task<ExecutionRun> ExecuteAsync(
        Experiment experiment,
        IProgress<StreamingUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExecutionRun run;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prepStart = _clock.UtcNow;
            var resolved = await ResolveContentAsync(experiment, cancellationToken).ConfigureAwait(false);
            var prepDuration = _clock.UtcNow - prepStart;

            var payload = new OpenAiRequestPayload
            {
                Model = experiment.Request.Model,
                ResolvedSystemPrompt = resolved.SystemPrompt,
                ResolvedCachedContextText = resolved.CachedContextText,
                ResolvedUserContextText = resolved.UserContextText,
                ReasoningEffort = experiment.Request.ReasoningEffort,
                MaxOutputTokens = experiment.Request.MaxOutputTokens,
                Stream = experiment.Request.Stream,
                PromptCacheKey = experiment.Request.PromptCacheKey,
                ResponseSchema = experiment.Request.ResponseSchema,
                AdditionalSettings = new Dictionary<string, string>(experiment.Request.AdditionalSettings),
                Snapshot = new RequestSnapshot
                {
                    Request = experiment.Request.Clone(),
                    ResolvedSystemPrompt = resolved.SystemPrompt,
                    ResolvedCachedContextText = resolved.CachedContextText,
                    ResolvedUserContextText = resolved.UserContextText,
                    FileHashesUsed = resolved.FileHashesUsed,
                    TakenAt = _clock.UtcNow
                }
            };

            run = await _client.ExecuteAsync(payload, progress, cancellationToken).ConfigureAwait(false);
            run.RequestPreparationDuration = prepDuration;
            ApplyCost(run);
        }
        catch (OperationCanceledException)
        {
            run = BuildTerminalRun(experiment, ExecutionStatus.Canceled, exception: null);
        }
        catch (Exception ex)
        {
            run = BuildTerminalRun(experiment, ExecutionStatus.Failed, ex);
        }

        experiment.Runs.Add(run);
        await _workspaceStore.SaveRunAsync(experiment.Id, run, CancellationToken.None).ConfigureAwait(false);
        return run;
    }

    private void ApplyCost(ExecutionRun run)
    {
        var pricing = _pricingProvider.TryGet(run.ActualModel ?? run.RequestedModel ?? string.Empty);
        if (pricing is null)
        {
            return;
        }

        var cost = _costCalculator.Calculate(run.Usage, pricing);
        run.EstimatedInputCost = cost.InputCost;
        run.EstimatedCachedInputCost = cost.CachedInputCost;
        run.EstimatedOutputCost = cost.OutputCost;
        run.EstimatedCost = cost.TotalCost;
    }

    private ExecutionRun BuildTerminalRun(Experiment experiment, ExecutionStatus status, Exception? exception)
    {
        var now = _clock.UtcNow;
        return new ExecutionRun
        {
            StartedAt = now,
            FinishedAt = now,
            Status = status,
            RequestedModel = experiment.Request.Model,
            Error = exception?.Message,
            ExceptionType = exception?.GetType().Name,
            Snapshot = new RequestSnapshot
            {
                Request = experiment.Request.Clone(),
                ResolvedSystemPrompt = string.Empty,
                ResolvedCachedContextText = string.Empty,
                ResolvedUserContextText = string.Empty,
                FileHashesUsed = new List<string>(),
                TakenAt = now
            }
        };
    }

    private async Task<ResolvedContent> ResolveContentAsync(Experiment experiment, CancellationToken cancellationToken)
    {
        var fileTextCache = new Dictionary<string, string>();
        var fileHashesUsed = new List<string>();

        async Task<string> GetFileTextAsync(string fileId)
        {
            if (fileTextCache.TryGetValue(fileId, out var cached))
            {
                return cached;
            }

            var file = experiment.Files.FirstOrDefault(f => f.Id == fileId);
            if (file is null)
            {
                return string.Empty;
            }

            fileHashesUsed.Add(file.Sha256);
            var absolutePath = _workspaceStore.GetFileAbsolutePath(experiment.Id, file);
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(file.ContentType, file.OriginalFileName));
            var text = extractor is null
                ? string.Empty
                : await extractor.ExtractTextAsync(absolutePath, file.ContentType, cancellationToken).ConfigureAwait(false);

            fileTextCache[fileId] = text;
            return text;
        }

        async Task<string> BuildContentSetTextAsync(ContentSet set)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(set.Text))
            {
                parts.Add(set.Text);
            }

            foreach (var fileId in set.FileIds)
            {
                var text = await GetFileTextAsync(fileId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join("\n\n", parts);
        }

        var cachedText = await BuildContentSetTextAsync(experiment.Request.CachedContext).ConfigureAwait(false);
        var userText = await BuildContentSetTextAsync(experiment.Request.UserContext).ConfigureAwait(false);

        var variableValues = new Dictionary<string, string>();
        foreach (var variable in experiment.Request.Variables)
        {
            variableValues[variable.Name] = variable.Kind switch
            {
                VariableBindingKind.Text => variable.TextValue ?? string.Empty,
                VariableBindingKind.File when variable.FileId is not null => await GetFileTextAsync(variable.FileId).ConfigureAwait(false),
                _ => string.Empty
            };
        }

        string Substitute(string text)
        {
            foreach (var (name, value) in variableValues)
            {
                text = text.Replace("{{" + name + "}}", value);
            }

            return text;
        }

        return new ResolvedContent(
            Substitute(experiment.Request.SystemPrompt),
            Substitute(cachedText),
            Substitute(userText),
            fileHashesUsed);
    }

    private sealed record ResolvedContent(string SystemPrompt, string CachedContextText, string UserContextText, List<string> FileHashesUsed);
}
