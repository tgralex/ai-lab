using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;

namespace OpenAiBench.Tests.Execution;

/// <summary>
/// Mimics the real ExperimentRunner's contract (never throws — cancellation and failures become a
/// terminal ExecutionRun) so ExecuteAllCoordinator can be tested without any real HTTP/IO dependency.
/// </summary>
internal sealed class FakeExperimentRunner : IExperimentRunner
{
    private readonly TimeSpan _workDuration;
    private readonly Action<Experiment>? _onStart;

    public List<Experiment> CallOrder { get; } = new();
    public int CurrentConcurrent;
    public int MaxObservedConcurrent;
    private readonly object _lock = new();

    public FakeExperimentRunner(TimeSpan? workDuration = null, Action<Experiment>? onStart = null)
    {
        _workDuration = workDuration ?? TimeSpan.Zero;
        _onStart = onStart;
    }

    public async Task<ExecutionRun> ExecuteAsync(Experiment experiment, IProgress<StreamingUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            CallOrder.Add(experiment);
            CurrentConcurrent++;
            MaxObservedConcurrent = Math.Max(MaxObservedConcurrent, CurrentConcurrent);
        }

        _onStart?.Invoke(experiment);

        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return TestHelpers.CreateRun(ExecutionStatus.Canceled);
            }

            try
            {
                await Task.Delay(_workDuration, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return TestHelpers.CreateRun(ExecutionStatus.Canceled);
            }

            return TestHelpers.CreateRun(ExecutionStatus.Completed);
        }
        finally
        {
            lock (_lock)
            {
                CurrentConcurrent--;
            }
        }
    }
}
