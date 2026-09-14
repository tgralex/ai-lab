using OpenAiBench.Core.Domain;

namespace OpenAiBench.Core.Execution;

public interface IExperimentRunner
{
    /// <summary>
    /// Executes one experiment and always returns a completed <see cref="ExecutionRun"/> — failures and
    /// cancellations are captured on the run rather than thrown, and the run is appended to
    /// <see cref="Experiment.Runs"/> and persisted before this returns.
    /// </summary>
    Task<ExecutionRun> ExecuteAsync(
        Experiment experiment,
        IProgress<StreamingUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
