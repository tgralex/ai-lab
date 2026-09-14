namespace AiLab.Infrastructure.Sse;

public sealed class SseEvent
{
    public string? EventName { get; init; }

    public required string Data { get; init; }
}
