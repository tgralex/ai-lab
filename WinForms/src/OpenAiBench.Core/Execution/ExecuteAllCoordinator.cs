using System.Diagnostics;
using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Statistics;

namespace OpenAiBench.Core.Execution;

public sealed class ExecuteAllCoordinator
{
    private readonly IExperimentRunner _runner;

    public ExecuteAllCoordinator(IExperimentRunner runner)
    {
        _runner = runner;
    }

    public async Task<ExecuteAllResult> ExecuteAllAsync(
        IReadOnlyList<Experiment> experiments,
        ExecutionMode mode,
        int maxConcurrency,
        IProgress<ExecuteAllProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var runs = mode == ExecutionMode.Sequential
            ? await ExecuteSequentialAsync(experiments, progress, cancellationToken).ConfigureAwait(false)
            : await ExecuteParallelAsync(experiments, maxConcurrency, progress, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var summary = BenchmarkAggregator.ComputeGlobalSummary(runs, stopwatch.Elapsed);
        return new ExecuteAllResult { Runs = runs, Summary = summary };
    }

    private async Task<List<ExecutionRun>> ExecuteSequentialAsync(
        IReadOnlyList<Experiment> experiments,
        IProgress<ExecuteAllProgress>? progress,
        CancellationToken cancellationToken)
    {
        var runs = new List<ExecutionRun>(experiments.Count);
        foreach (var experiment in experiments)
        {
            runs.Add(await RunOneAsync(experiment, progress, cancellationToken).ConfigureAwait(false));
        }

        return runs;
    }

    private async Task<List<ExecutionRun>> ExecuteParallelAsync(
        IReadOnlyList<Experiment> experiments,
        int maxConcurrency,
        IProgress<ExecuteAllProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var tasks = experiments.Select(async experiment =>
        {
            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancel-All fired while this run was still queued behind the concurrency limit —
                // let the runner itself record it as Canceled (it never touches the network).
                return await RunOneAsync(experiment, progress, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await RunOneAsync(experiment, progress, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    private async Task<ExecutionRun> RunOneAsync(
        Experiment experiment,
        IProgress<ExecuteAllProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ExecuteAllProgress { ExperimentId = experiment.Id, Status = ExecutionStatus.Running });
        var run = await _runner.ExecuteAsync(experiment, progress: null, cancellationToken).ConfigureAwait(false);
        progress?.Report(new ExecuteAllProgress { ExperimentId = experiment.Id, Status = run.Status });
        return run;
    }
}
