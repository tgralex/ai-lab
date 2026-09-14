using System.Net;

namespace AiLab.Infrastructure;

/// <summary>
/// Bounded exponential backoff for transient failures (HttpRequestException, 429, 5xx) that occur
/// before a response stream starts — ported from the WinForms RetryPolicyExecutor. Retry time is
/// counted toward reported latency, never hidden from the caller.
/// </summary>
public sealed class RetryPolicyExecutor(int maxAttempts = 3, int baseDelayMs = 500)
{
    public int LastRetryCount { get; private set; }

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendRequest,
        CancellationToken ct)
    {
        LastRetryCount = 0;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await sendRequest(ct);
                if (!IsTransientFailure(response.StatusCode) || attempt == maxAttempts)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                lastException = ex;
            }

            LastRetryCount = attempt;
            var delayMs = baseDelayMs * (1 << (attempt - 1));
            await Task.Delay(delayMs, ct);
        }

        throw lastException ?? new InvalidOperationException("Retry loop exited without a response or exception.");
    }

    private static bool IsTransientFailure(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
