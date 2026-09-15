using System.Text.RegularExpressions;
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

        var models = await db.ProviderModels.AsNoTracking().ToListAsync(ct);
        return ApplyPresentation(models);
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

    // Matches a trailing dated-snapshot suffix like "-2025-08-07" (OpenAI pins a snapshot behind
    // both a rolling alias, e.g. "gpt-5", and dated ids, e.g. "gpt-5-2025-08-07" — every provider's
    // live model list accumulates these over time).
    private static readonly Regex DatedSnapshotSuffix = new(@"-(\d{4})-(\d{2})-(\d{2})$", RegexOptions.Compiled);

    // Older chat-capable OpenAI generations that stay listed indefinitely — kept selectable, just
    // deprioritized (unlike the non-chat families OpenAiProvider excludes outright). The anchor
    // ('-' or end-of-string) after "gpt-4" is what keeps this from also matching gpt-4.1 / gpt-4o.
    private static readonly Regex LegacyOpenAiFamily = new(@"^(gpt-3\.5|gpt-4(-|$))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Presentation-only pass applied on every read (never persisted): flags dated snapshots that
    /// have a still-current undated alias, and known-legacy OpenAI families, as deprecated so the
    /// UI can hide them behind an opt-in toggle — then sorts newest-first using whatever dated
    /// snapshot exists for a family as its recency signal (a family with no dated snapshot yet is
    /// assumed newest, which is correct for e.g. a same-day release).
    /// </summary>
    private static IReadOnlyList<ProviderModel> ApplyPresentation(List<ProviderModel> models)
    {
        var annotated = new List<(ProviderModel Model, DateOnly SortDate)>(models.Count);

        foreach (var group in models.GroupBy(m => m.ProviderId))
        {
            var idSet = group.Select(m => m.ModelId).ToHashSet(StringComparer.Ordinal);

            foreach (var m in group)
            {
                var selfSnapshot = DatedSnapshotSuffix.Match(m.ModelId);
                var isDatedSnapshotOfExisting = selfSnapshot.Success && idSet.Contains(m.ModelId[..selfSnapshot.Index]);
                var isLegacyFamily = m.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase) && LegacyOpenAiFamily.IsMatch(m.ModelId);

                var sortDate = selfSnapshot.Success
                    ? ParseSnapshotDate(selfSnapshot)
                    : FindLatestSnapshotDate(m.ModelId, idSet) ?? (isLegacyFamily ? DateOnly.MinValue : DateOnly.MaxValue);

                var effective = isDatedSnapshotOfExisting || isLegacyFamily ? m with { IsDeprecated = true } : m;
                annotated.Add((effective, sortDate));
            }
        }

        return annotated
            .OrderBy(a => a.Model.ProviderId, StringComparer.Ordinal)
            .ThenByDescending(a => a.SortDate)
            .ThenByDescending(a => a.Model.Recommended)
            .ThenBy(a => a.Model.DisplayName, StringComparer.Ordinal)
            .Select(a => a.Model)
            .ToList();
    }

    private static DateOnly ParseSnapshotDate(Match match) =>
        new(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));

    /// <summary>The latest dated-snapshot sibling's date for an undated alias (e.g. "gpt-5" from "gpt-5-2025-08-07"), if any exist.</summary>
    private static DateOnly? FindLatestSnapshotDate(string baseId, IReadOnlySet<string> allIds)
    {
        DateOnly? latest = null;
        foreach (var id in allIds)
        {
            if (!id.StartsWith(baseId + "-", StringComparison.Ordinal))
            {
                continue;
            }

            var match = DatedSnapshotSuffix.Match(id);
            if (!match.Success || match.Index != baseId.Length)
            {
                continue;
            }

            var date = ParseSnapshotDate(match);
            if (latest is null || date > latest)
            {
                latest = date;
            }
        }

        return latest;
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
