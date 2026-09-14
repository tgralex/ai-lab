namespace AiLab.Core.Requests;

public enum FailurePolicy
{
    FailPlan,
    Retry,
    ContinueWithError,
}

public sealed class RetryPolicy
{
    public int MaxRetries { get; init; } = 3;

    public int BaseBackoffMs { get; init; } = 500;
}
