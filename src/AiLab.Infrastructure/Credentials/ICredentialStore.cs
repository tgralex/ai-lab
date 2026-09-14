namespace AiLab.Infrastructure.Credentials;

public sealed class ProviderCredentialStatus
{
    public required bool Configured { get; init; }
}

/// <summary>
/// Owns provider API keys. Never exposes a key to a caller outside Infrastructure — only
/// GetAllStatusAsync's {providerId: {configured}} shape is safe to serialize back to the frontend.
/// </summary>
public interface ICredentialStore
{
    Task<ProviderCredentialStatus> GetStatusAsync(string providerId, CancellationToken ct);

    Task<IReadOnlyDictionary<string, ProviderCredentialStatus>> GetAllStatusAsync(CancellationToken ct);

    Task SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct);

    Task<string?> GetApiKeyAsync(string providerId, CancellationToken ct);

    event EventHandler? CredentialsChanged;
}
