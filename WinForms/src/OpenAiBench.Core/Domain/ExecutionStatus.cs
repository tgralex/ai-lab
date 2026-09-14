namespace OpenAiBench.Core.Domain;

public enum ExecutionStatus
{
    Idle,
    Running,
    Streaming,
    Completed,
    Failed,
    Canceled
}
