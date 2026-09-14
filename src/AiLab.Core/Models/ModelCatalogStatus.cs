namespace AiLab.Core.Models;

public sealed class ModelCatalogProviderStatus
{
    public required string ProviderId { get; init; }

    public DateTimeOffset? LastRefreshAt { get; set; }

    public ModelRefreshSource RefreshSource { get; set; } = ModelRefreshSource.SeedOnly;

    /// <summary>"Ok" or "Failed: &lt;reason&gt;". A failed refresh never clears the previously-cached catalog rows.</summary>
    public string RefreshStatus { get; set; } = "Ok";

    public bool IsStale(TimeSpan maxAge) => !LastRefreshAt.HasValue || DateTimeOffset.UtcNow - LastRefreshAt.Value > maxAge;
}

public sealed class ModelCatalogRefreshResult
{
    public required string ProviderId { get; init; }

    public required bool Success { get; init; }

    public int ModelsFound { get; init; }

    public string? ErrorMessage { get; init; }
}
