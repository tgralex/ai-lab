using AiLab.Core.Models;
using AiLab.Core.Providers;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiLab.Infrastructure.ModelCatalog;

/// <summary>
/// Merges two sources per model: a live list from the provider's API (id, sometimes context
/// length — no pricing) and a hand-maintained local seed file (full capability + pricing metadata,
/// since neither provider's API exposes that). Cached in SQLite; auto-refreshes entries older than
/// 7 days but a failed refresh never destroys the last good catalog.
/// </summary>
public sealed class ProviderModelCatalogService(AiLabDbContext db, IEnumerable<IAiProvider> providers) : IProviderModelCatalogService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<ProviderModel>> GetCatalogAsync(CancellationToken ct)
    {
        var staleProviders = new List<string>();
        foreach (var provider in providers)
        {
            var status = await db.ModelCatalogProviderStatuses.FindAsync([provider.Id], ct);
            if (status is null || status.IsStale(MaxAge))
            {
                staleProviders.Add(provider.Id);
            }
        }

        foreach (var providerId in staleProviders)
        {
            await RefreshOneAsync(providerId, ct);
        }

        return await db.ProviderModels.AsNoTracking().OrderBy(m => m.ProviderId).ThenBy(m => m.DisplayName).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, ModelCatalogProviderStatus>> GetStatusAsync(CancellationToken ct)
    {
        var statuses = await db.ModelCatalogProviderStatuses.AsNoTracking().ToListAsync(ct);
        return statuses.ToDictionary(s => s.ProviderId, s => s);
    }

    public async Task<IReadOnlyList<ModelCatalogRefreshResult>> RefreshAsync(string? providerId, CancellationToken ct)
    {
        var targetProviderIds = providerId is null ? providers.Select(p => p.Id).ToList() : [providerId];
        var results = new List<ModelCatalogRefreshResult>();

        foreach (var id in targetProviderIds)
        {
            results.Add(await RefreshOneAsync(id, ct));
        }

        return results;
    }

    private async Task<ModelCatalogRefreshResult> RefreshOneAsync(string providerId, CancellationToken ct)
    {
        var seedModels = ModelSeedLoader.Load(providerId);
        var provider = providers.FirstOrDefault(p => p.Id == providerId);

        try
        {
            var liveModels = provider is null ? [] : await provider.GetModelsAsync(ct);
            var merged = MergeSeedWithLive(seedModels, liveModels);

            var existing = db.ProviderModels.Where(m => m.ProviderId == providerId);
            db.ProviderModels.RemoveRange(existing);
            db.ProviderModels.AddRange(merged);

            await UpsertStatusAsync(
                providerId,
                DateTimeOffset.UtcNow,
                liveModels.Count > 0 ? ModelRefreshSource.Mixed : ModelRefreshSource.SeedOnly,
                "Ok",
                ct);

            await db.SaveChangesAsync(ct);
            return new ModelCatalogRefreshResult { ProviderId = providerId, Success = true, ModelsFound = merged.Count };
        }
        catch (Exception ex)
        {
            // Preserve whatever's already cached; only seed for the very first refresh so the catalog isn't empty.
            var hasExisting = await db.ProviderModels.AnyAsync(m => m.ProviderId == providerId, ct);
            if (!hasExisting)
            {
                db.ProviderModels.AddRange(seedModels);
            }

            var existingStatus = await db.ModelCatalogProviderStatuses.FindAsync([providerId], ct);
            await UpsertStatusAsync(
                providerId,
                hasExisting ? existingStatus?.LastRefreshAt : DateTimeOffset.UtcNow,
                ModelRefreshSource.SeedOnly,
                $"Failed: {ex.Message}",
                ct);

            await db.SaveChangesAsync(ct);
            return new ModelCatalogRefreshResult { ProviderId = providerId, Success = false, ModelsFound = seedModels.Count, ErrorMessage = ex.Message };
        }
    }

    private static List<ProviderModel> MergeSeedWithLive(IReadOnlyList<ProviderModel> seedModels, IReadOnlyList<ProviderModel> liveModels)
    {
        var liveIds = liveModels.Select(m => m.ModelId).ToHashSet();
        var seedIds = seedModels.Select(m => m.ModelId).ToHashSet();

        var result = seedModels
            .Select(seed => liveIds.Contains(seed.ModelId) ? seed with { Source = ModelRefreshSource.Mixed } : seed)
            .ToList();

        result.AddRange(liveModels.Where(live => !seedIds.Contains(live.ModelId)).Select(live => live with { Source = ModelRefreshSource.Live }));

        return result;
    }

    private async Task UpsertStatusAsync(string providerId, DateTimeOffset? lastRefreshAt, ModelRefreshSource source, string status, CancellationToken ct)
    {
        var existing = await db.ModelCatalogProviderStatuses.FindAsync([providerId], ct);
        if (existing is null)
        {
            db.ModelCatalogProviderStatuses.Add(new ModelCatalogProviderStatus
            {
                ProviderId = providerId,
                LastRefreshAt = lastRefreshAt,
                RefreshSource = source,
                RefreshStatus = status,
            });
        }
        else
        {
            existing.LastRefreshAt = lastRefreshAt;
            existing.RefreshSource = source;
            existing.RefreshStatus = status;
        }
    }
}
