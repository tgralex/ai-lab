namespace AiLab.Core.Execution;

public sealed class TokenUsage
{
    public int InputTokens { get; init; }

    public int CachedInputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public int TotalTokens { get; init; }

    public int UncachedInputTokens => Math.Max(0, InputTokens - CachedInputTokens);

    public double CacheHitPercentage => InputTokens == 0 ? 0 : (double)CachedInputTokens / InputTokens * 100;
}
