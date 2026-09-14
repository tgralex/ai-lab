using AiLab.Core.Models;

namespace AiLab.Core.Providers;

public interface IAiProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<ProviderExecutionResult> ExecuteAsync(
        AiRequestExecutionContext context,
        IProgress<AiStreamEvent>? progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderModel>> GetModelsAsync(CancellationToken cancellationToken);
}
