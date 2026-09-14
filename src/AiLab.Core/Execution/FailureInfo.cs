namespace AiLab.Core.Execution;

public sealed class FailureInfo
{
    public string? ExceptionType { get; init; }

    public string? Message { get; init; }

    public int? HttpStatus { get; init; }

    public string? ProviderError { get; init; }

    public bool StreamingStarted { get; init; }

    public int RetryCount { get; init; }
}
