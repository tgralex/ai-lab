using AiLab.Core.Execution;
using AiLab.Core.Models;
using AiLab.Core.Providers;

namespace AiLab.Tests.TestDoubles;

/// <summary>Configurable IAiProvider test double — emits a scripted event sequence then returns a scripted result.</summary>
public sealed class FakeAiProvider(string id = "openai") : IAiProvider
{
    public string Id { get; } = id;

    public string DisplayName => Id;

    public int CallCount { get; private set; }

    public int MaxConcurrentCalls { get; private set; }

    private int _currentConcurrentCalls;
    private readonly Lock _lock = new();

    public Func<AiRequestExecutionContext, IProgress<AiStreamEvent>?, CancellationToken, Task<ProviderExecutionResult>>? Behavior { get; set; }

    public TimeSpan SimulatedDelay { get; set; } = TimeSpan.Zero;

    public ProviderExecutionResult ResultToReturn { get; set; } = new()
    {
        Success = true,
        OutputText = "fake output",
        Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 },
    };

    public async Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            CallCount++;
            _currentConcurrentCalls++;
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, _currentConcurrentCalls);
        }

        try
        {
            if (Behavior is not null)
            {
                return await Behavior(context, progress, cancellationToken);
            }

            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Started));

            if (SimulatedDelay > TimeSpan.Zero)
            {
                await Task.Delay(SimulatedDelay, cancellationToken);
            }

            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.FirstProtocolEvent));
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.OutputTextDelta, textDelta: ResultToReturn.OutputText));
            progress?.Report(AiStreamEvent.Create(AiStreamEventKind.Completed));

            return ResultToReturn;
        }
        finally
        {
            lock (_lock)
            {
                _currentConcurrentCalls--;
            }
        }
    }

    public Task<IReadOnlyList<ProviderModel>> GetModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProviderModel>>([]);
}
