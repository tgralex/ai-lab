using OpenAiBench.Core.Domain;

namespace OpenAiBench.Tests;

internal static class TestHelpers
{
    public static RequestSnapshot CreateSnapshot(RequestDefinition? request = null) => new()
    {
        Request = request ?? new RequestDefinition { Model = "gpt-4.1" },
        ResolvedSystemPrompt = string.Empty,
        ResolvedCachedContextText = string.Empty,
        ResolvedUserContextText = string.Empty,
        FileHashesUsed = new List<string>(),
        TakenAt = DateTimeOffset.UtcNow
    };

    public static ExecutionRun CreateRun(
        ExecutionStatus status = ExecutionStatus.Completed,
        TimeSpan? totalDuration = null,
        TimeSpan? timeToFirstToken = null,
        TimeSpan? generationDuration = null,
        TokenUsage? usage = null,
        decimal? estimatedCost = null) => new()
    {
        Snapshot = CreateSnapshot(),
        Status = status,
        TotalDuration = totalDuration ?? TimeSpan.FromSeconds(1),
        TimeToFirstToken = timeToFirstToken,
        GenerationDuration = generationDuration,
        Usage = usage ?? TokenUsage.Empty,
        EstimatedCost = estimatedCost
    };
}
