namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>
/// Distinguishes the first SSE event of any kind (a control/metadata event, e.g. response.created)
/// from the first genuinely visible output token (a non-empty response.output_text.delta) — reasoning
/// models routinely emit several control events before any visible text.
/// </summary>
public sealed class StreamingTimingTracker
{
    public DateTimeOffset? FirstResponseEventAt { get; private set; }
    public DateTimeOffset? FirstTokenAt { get; private set; }

    public void OnEvent(DateTimeOffset timestamp) => FirstResponseEventAt ??= timestamp;

    public void OnOutputTextDelta(DateTimeOffset timestamp, string? delta)
    {
        FirstResponseEventAt ??= timestamp;
        if (FirstTokenAt is null && !string.IsNullOrEmpty(delta))
        {
            FirstTokenAt = timestamp;
        }
    }
}
