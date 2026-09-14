namespace AiLab.Core.Providers;

public enum AiStreamEventKind
{
    Started,
    FirstProtocolEvent,
    OutputTextDelta,
    ReasoningDelta,
    ToolCallDelta,
    UsageUpdate,
    Completed,
    Error,
}

/// <summary>
/// Discriminated event emitted while a provider call is in flight. The engine uses
/// <see cref="AiStreamEventKind.FirstProtocolEvent"/> vs. <see cref="AiStreamEventKind.OutputTextDelta"/>
/// to distinguish "first byte off the wire" from "first user-visible output token" (TTFT).
/// </summary>
public sealed class AiStreamEvent
{
    public required AiStreamEventKind Kind { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? TextDelta { get; init; }

    public string? ErrorMessage { get; init; }

    public static AiStreamEvent Create(AiStreamEventKind kind, string? textDelta = null, string? errorMessage = null) =>
        new()
        {
            Kind = kind,
            Timestamp = DateTimeOffset.UtcNow,
            TextDelta = textDelta,
            ErrorMessage = errorMessage,
        };
}
