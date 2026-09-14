using System.Net;

namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>Bounded exponential backoff for transient connection/5xx/429 failures that happen before streaming begins.</summary>
public sealed class RetryPolicyExecutor
{
    public int MaxAttempts { get; }
    public TimeSpan BaseDelay { get; }

    public RetryPolicyExecutor(int maxAttempts = 3, TimeSpan? baseDelay = null)
    {
        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public static bool IsTransientStatus(HttpStatusCode statusCode) =>
        (int)statusCode == 429 || (int)statusCode >= 500;

    public TimeSpan GetDelay(int attemptIndex) =>
        TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(2, attemptIndex));
}
