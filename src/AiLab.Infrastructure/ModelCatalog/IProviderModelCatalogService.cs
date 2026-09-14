using AiLab.Core.Models;

namespace AiLab.Infrastructure.ModelCatalog;

public interface IProviderModelCatalogService
{
    /// <summary>Serves from the local cache, triggering a background-safe refresh first if any provider's entry is older than 7 days.</summary>
    Task<IReadOnlyList<ProviderModel>> GetCatalogAsync(CancellationToken ct);

    /// <summary>Pass null to refresh every registered provider, or a specific provider id for "Refresh Models Now" on one provider.</summary>
    Task<IReadOnlyList<ModelCatalogRefreshResult>> RefreshAsync(string? providerId, CancellationToken ct);

    Task<IReadOnlyDictionary<string, ModelCatalogProviderStatus>> GetStatusAsync(CancellationToken ct);
}
