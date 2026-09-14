using OpenAiBench.Core.Domain;

namespace OpenAiBench.Infrastructure.Persistence;

public static class PersistenceMapper
{
    public static ExperimentFileDto ToDto(Experiment experiment) => new()
    {
        Id = experiment.Id,
        Name = experiment.Name,
        Tags = new List<string>(experiment.Tags),
        Notes = experiment.Notes,
        Order = experiment.Order,
        CreatedAt = experiment.CreatedAt,
        Request = ToDto(experiment.Request),
        Files = experiment.Files.Select(ToDto).ToList(),
        RunIds = experiment.Runs.Select(r => r.Id).ToList()
    };

    public static Experiment FromDto(ExperimentFileDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Tags = new List<string>(dto.Tags),
        Notes = dto.Notes,
        Order = dto.Order,
        CreatedAt = dto.CreatedAt,
        Request = FromDto(dto.Request),
        Files = dto.Files.Select(FromDto).ToList(),
        Runs = new List<ExecutionRun>()
    };

    public static RequestDefinitionDto ToDto(RequestDefinition request) => new()
    {
        Model = request.Model,
        SystemPrompt = request.SystemPrompt,
        CachedContext = ToDto(request.CachedContext),
        UserContext = ToDto(request.UserContext),
        Variables = request.Variables.Select(ToDto).ToList(),
        ResponseSchema = request.ResponseSchema,
        ReasoningEffort = request.ReasoningEffort,
        MaxOutputTokens = request.MaxOutputTokens,
        Stream = request.Stream,
        PromptCacheKey = request.PromptCacheKey,
        AdditionalSettings = new Dictionary<string, string>(request.AdditionalSettings)
    };

    public static RequestDefinition FromDto(RequestDefinitionDto dto) => new()
    {
        Model = dto.Model,
        SystemPrompt = dto.SystemPrompt,
        CachedContext = FromDto(dto.CachedContext),
        UserContext = FromDto(dto.UserContext),
        Variables = dto.Variables.Select(FromDto).ToList(),
        ResponseSchema = dto.ResponseSchema,
        ReasoningEffort = dto.ReasoningEffort,
        MaxOutputTokens = dto.MaxOutputTokens,
        Stream = dto.Stream,
        PromptCacheKey = dto.PromptCacheKey,
        AdditionalSettings = new Dictionary<string, string>(dto.AdditionalSettings)
    };

    public static ContentSetDto ToDto(ContentSet set) => new() { Text = set.Text, FileIds = new List<string>(set.FileIds) };

    public static ContentSet FromDto(ContentSetDto dto) => new() { Text = dto.Text, FileIds = new List<string>(dto.FileIds) };

    public static VariableBindingDto ToDto(VariableBinding binding) => new()
    {
        Name = binding.Name,
        Kind = binding.Kind.ToString(),
        TextValue = binding.TextValue,
        FileId = binding.FileId
    };

    public static VariableBinding FromDto(VariableBindingDto dto) => new()
    {
        Name = dto.Name,
        Kind = Enum.TryParse<VariableBindingKind>(dto.Kind, out var kind) ? kind : VariableBindingKind.Text,
        TextValue = dto.TextValue,
        FileId = dto.FileId
    };

    public static AttachedFileDto ToDto(AttachedFileRef file) => new()
    {
        Id = file.Id,
        OriginalFileName = file.OriginalFileName,
        StoredRelativePath = file.StoredRelativePath,
        SizeBytes = file.SizeBytes,
        ContentType = file.ContentType,
        Sha256 = file.Sha256,
        AddedAt = file.AddedAt
    };

    public static AttachedFileRef FromDto(AttachedFileDto dto) => new()
    {
        Id = dto.Id,
        OriginalFileName = dto.OriginalFileName,
        StoredRelativePath = dto.StoredRelativePath,
        SizeBytes = dto.SizeBytes,
        ContentType = dto.ContentType,
        Sha256 = dto.Sha256,
        AddedAt = dto.AddedAt
    };

    public static RunFileDto ToDto(ExecutionRun run) => new()
    {
        Id = run.Id,
        StartedAt = run.StartedAt,
        FirstResponseEventAt = run.FirstResponseEventAt,
        FirstTokenAt = run.FirstTokenAt,
        FinishedAt = run.FinishedAt,
        TotalDurationMs = run.TotalDuration.TotalMilliseconds,
        TimeToFirstResponseEventMs = run.TimeToFirstResponseEvent?.TotalMilliseconds,
        TimeToFirstTokenMs = run.TimeToFirstToken?.TotalMilliseconds,
        GenerationDurationMs = run.GenerationDuration?.TotalMilliseconds,
        RequestPreparationDurationMs = run.RequestPreparationDuration?.TotalMilliseconds,
        FileProcessingDurationMs = run.FileProcessingDuration?.TotalMilliseconds,
        InputTokens = run.Usage.InputTokens,
        CachedInputTokens = run.Usage.CachedInputTokens,
        OutputTokens = run.Usage.OutputTokens,
        ReasoningTokens = run.Usage.ReasoningTokens,
        TotalTokens = run.Usage.TotalTokens,
        EstimatedInputCost = run.EstimatedInputCost,
        EstimatedCachedInputCost = run.EstimatedCachedInputCost,
        EstimatedOutputCost = run.EstimatedOutputCost,
        EstimatedCost = run.EstimatedCost,
        Output = run.Output,
        RawResponse = run.RawResponse,
        RawRequest = run.RawRequest,
        ResponseId = run.ResponseId,
        RequestedModel = run.RequestedModel,
        ActualModel = run.ActualModel,
        Status = run.Status.ToString(),
        FinishReason = run.FinishReason,
        Error = run.Error,
        ExceptionType = run.ExceptionType,
        HttpStatus = run.HttpStatus,
        ApiErrorBody = run.ApiErrorBody,
        FailedAfterStreamingBegan = run.FailedAfterStreamingBegan,
        RetryCount = run.RetryCount,
        RetryDelaysMs = run.RetryDelays.Select(d => d.TotalMilliseconds).ToList(),
        RequestByteSize = run.RequestByteSize,
        ResponseByteSize = run.ResponseByteSize,
        Snapshot = ToDto(run.Snapshot)
    };

    public static ExecutionRun FromDto(RunFileDto dto) => new()
    {
        Id = dto.Id,
        StartedAt = dto.StartedAt,
        FirstResponseEventAt = dto.FirstResponseEventAt,
        FirstTokenAt = dto.FirstTokenAt,
        FinishedAt = dto.FinishedAt,
        TotalDuration = TimeSpan.FromMilliseconds(dto.TotalDurationMs),
        TimeToFirstResponseEvent = dto.TimeToFirstResponseEventMs is { } t1 ? TimeSpan.FromMilliseconds(t1) : null,
        TimeToFirstToken = dto.TimeToFirstTokenMs is { } t2 ? TimeSpan.FromMilliseconds(t2) : null,
        GenerationDuration = dto.GenerationDurationMs is { } t3 ? TimeSpan.FromMilliseconds(t3) : null,
        RequestPreparationDuration = dto.RequestPreparationDurationMs is { } t4 ? TimeSpan.FromMilliseconds(t4) : null,
        FileProcessingDuration = dto.FileProcessingDurationMs is { } t5 ? TimeSpan.FromMilliseconds(t5) : null,
        Usage = new TokenUsage
        {
            InputTokens = dto.InputTokens,
            CachedInputTokens = dto.CachedInputTokens,
            OutputTokens = dto.OutputTokens,
            ReasoningTokens = dto.ReasoningTokens,
            TotalTokens = dto.TotalTokens
        },
        EstimatedInputCost = dto.EstimatedInputCost,
        EstimatedCachedInputCost = dto.EstimatedCachedInputCost,
        EstimatedOutputCost = dto.EstimatedOutputCost,
        EstimatedCost = dto.EstimatedCost,
        Output = dto.Output,
        RawResponse = dto.RawResponse,
        RawRequest = dto.RawRequest,
        ResponseId = dto.ResponseId,
        RequestedModel = dto.RequestedModel,
        ActualModel = dto.ActualModel,
        Status = Enum.TryParse<ExecutionStatus>(dto.Status, out var status) ? status : ExecutionStatus.Idle,
        FinishReason = dto.FinishReason,
        Error = dto.Error,
        ExceptionType = dto.ExceptionType,
        HttpStatus = dto.HttpStatus,
        ApiErrorBody = dto.ApiErrorBody,
        FailedAfterStreamingBegan = dto.FailedAfterStreamingBegan,
        RetryCount = dto.RetryCount,
        RetryDelays = dto.RetryDelaysMs.Select(TimeSpan.FromMilliseconds).ToList(),
        RequestByteSize = dto.RequestByteSize,
        ResponseByteSize = dto.ResponseByteSize,
        Snapshot = FromDto(dto.Snapshot)
    };

    public static RequestSnapshotDto ToDto(RequestSnapshot snapshot) => new()
    {
        Request = ToDto(snapshot.Request),
        ResolvedSystemPrompt = snapshot.ResolvedSystemPrompt,
        ResolvedCachedContextText = snapshot.ResolvedCachedContextText,
        ResolvedUserContextText = snapshot.ResolvedUserContextText,
        FileHashesUsed = new List<string>(snapshot.FileHashesUsed),
        TakenAt = snapshot.TakenAt
    };

    public static RequestSnapshot FromDto(RequestSnapshotDto dto) => new()
    {
        Request = FromDto(dto.Request),
        ResolvedSystemPrompt = dto.ResolvedSystemPrompt,
        ResolvedCachedContextText = dto.ResolvedCachedContextText,
        ResolvedUserContextText = dto.ResolvedUserContextText,
        FileHashesUsed = new List<string>(dto.FileHashesUsed),
        TakenAt = dto.TakenAt
    };
}
