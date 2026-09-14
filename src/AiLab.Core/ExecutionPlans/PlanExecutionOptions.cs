namespace AiLab.Core.ExecutionPlans;

public sealed class PlanExecutionOptions
{
    public int GlobalMaxConcurrency { get; init; } = 8;

    /// <summary>Per-provider cap (e.g. {"openai": 4, "anthropic": 3}) — a request only starts once both this and the global permit are free. Unlisted providers are bounded by the global cap alone.</summary>
    public IReadOnlyDictionary<string, int> ProviderMaxConcurrency { get; init; } = new Dictionary<string, int>();
}
