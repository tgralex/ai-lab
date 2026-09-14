using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Execution;

public sealed class ExecuteAllProgress
{
    public required Guid ExperimentId { get; init; }
    public required ExecutionStatus Status { get; init; }
}
