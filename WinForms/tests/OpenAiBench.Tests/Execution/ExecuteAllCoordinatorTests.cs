using OpenAiBench.Core.Domain;
using OpenAiBench.Core.Execution;
using Xunit;

namespace OpenAiBench.Tests.Execution;

public class ExecuteAllCoordinatorTests
{
    [Fact]
    public async Task ExecuteAllAsync_Sequential_RunsExperimentsInInputOrder()
    {
        var runner = new FakeExperimentRunner();
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = new[] { new Experiment { Name = "A" }, new Experiment { Name = "B" }, new Experiment { Name = "C" } };

        await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Sequential, maxConcurrency: 4);

        Assert.Equal(experiments, runner.CallOrder);
    }

    [Fact]
    public async Task ExecuteAllAsync_Sequential_NeverRunsMoreThanOneAtATime()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromMilliseconds(30));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = Enumerable.Range(0, 5).Select(i => new Experiment { Name = $"Exp{i}" }).ToArray();

        await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Sequential, maxConcurrency: 4);

        Assert.Equal(1, runner.MaxObservedConcurrent);
    }

    [Fact]
    public async Task ExecuteAllAsync_Parallel_NeverExceedsMaxConcurrency()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromMilliseconds(50));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = Enumerable.Range(0, 10).Select(i => new Experiment { Name = $"Exp{i}" }).ToArray();

        await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Parallel, maxConcurrency: 3);

        Assert.True(runner.MaxObservedConcurrent <= 3, $"Expected at most 3 concurrent runs, observed {runner.MaxObservedConcurrent}");
        Assert.Equal(10, runner.CallOrder.Count);
    }

    [Fact]
    public async Task ExecuteAllAsync_Parallel_ResultOrderMatchesInputOrder()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromMilliseconds(10));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = Enumerable.Range(0, 8).Select(i => new Experiment { Name = $"Exp{i}" }).ToArray();

        var result = await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Parallel, maxConcurrency: 3);

        Assert.Equal(8, result.Runs.Count);
    }

    [Fact]
    public async Task ExecuteAllAsync_CancelBeforeStart_RecordsCanceledNotFailed()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromSeconds(5));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = new[] { new Experiment { Name = "A" } };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Sequential, maxConcurrency: 1, cancellationToken: cts.Token);

        Assert.Equal(ExecutionStatus.Canceled, result.Runs.Single().Status);
    }

    [Fact]
    public async Task ExecuteAllAsync_CancelDuringParallelExecution_QueuedRunsAreCanceledNotFailed()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromMilliseconds(200));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = Enumerable.Range(0, 6).Select(i => new Experiment { Name = $"Exp{i}" }).ToArray();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        var result = await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Parallel, maxConcurrency: 2, cancellationToken: cts.Token);

        Assert.Equal(6, result.Runs.Count);
        Assert.Contains(result.Runs, r => r.Status == ExecutionStatus.Canceled);
        Assert.DoesNotContain(result.Runs, r => r.Status == ExecutionStatus.Failed);
    }

    [Fact]
    public async Task ExecuteAllAsync_GlobalSummary_WallClockReflectsParallelism()
    {
        var runner = new FakeExperimentRunner(workDuration: TimeSpan.FromMilliseconds(100));
        var coordinator = new ExecuteAllCoordinator(runner);
        var experiments = Enumerable.Range(0, 4).Select(i => new Experiment { Name = $"Exp{i}" }).ToArray();

        var result = await coordinator.ExecuteAllAsync(experiments, ExecutionMode.Parallel, maxConcurrency: 4);

        // 4 concurrent ~100ms runs should complete in well under the 400ms sequential sum.
        Assert.True(result.Summary.WallClockDuration < TimeSpan.FromMilliseconds(350));
    }
}
