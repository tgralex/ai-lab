using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Statistics;

namespace OpenAiBench.Core.Execution;

public sealed class ExecuteAllResult
{
    public required IReadOnlyList<ExecutionRun> Runs { get; init; }
    public required GlobalExecutionSummary Summary { get; init; }
}
