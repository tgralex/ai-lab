namespace OpenAiBench.Infrastructure.OpenAi;

public sealed class SseEvent
{
    public string? EventName { get; init; }
    public required string Data { get; init; }
}
