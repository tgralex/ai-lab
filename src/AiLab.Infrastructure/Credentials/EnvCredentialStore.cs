using System.Collections.Concurrent;

namespace AiLab.Infrastructure.Credentials;

/// <summary>
/// V1 ICredentialStore: real environment variable takes precedence over the .env file in the data
/// directory. SetApiKeyAsync updates the .env file and raises CredentialsChanged so providers pick
/// up the new key on their next call — no restart required. Ported from the WinForms ApiKeyProvider,
/// extended to multiple providers.
/// </summary>
public sealed class EnvCredentialStore : ICredentialStore
{
    /// <summary>
    /// Candidate env var names per provider, checked in order — lets a user's own natural naming
    /// choice (e.g. GROK_API_KEY) work alongside the documented canonical name (XAI_API_KEY)
    /// without forcing a rename. SetApiKeyAsync always writes the first (canonical) name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ProviderEnvVarNames = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = ["OPENAI_API_KEY"],
        ["anthropic"] = ["ANTHROPIC_API_KEY"],
        ["grok"] = ["XAI_API_KEY", "GROK_API_KEY"],
    };

    private readonly string _envFilePath;
    private readonly ConcurrentDictionary<string, string> _envFileValues;

    public event EventHandler? CredentialsChanged;

    public EnvCredentialStore(string dataDirectory)
    {
        _envFilePath = Path.Combine(dataDirectory, ".env");
        _envFileValues = new ConcurrentDictionary<string, string>(EnvFileLoader.Load(_envFilePath), StringComparer.OrdinalIgnoreCase);
    }

    public Task<ProviderCredentialStatus> GetStatusAsync(string providerId, CancellationToken ct)
    {
        var key = ResolveKey(providerId);
        return Task.FromResult(new ProviderCredentialStatus { Configured = !string.IsNullOrEmpty(key) });
    }

    public Task<IReadOnlyDictionary<string, ProviderCredentialStatus>> GetAllStatusAsync(CancellationToken ct)
    {
        var result = ProviderEnvVarNames.Keys.ToDictionary(
            providerId => providerId,
            providerId => new ProviderCredentialStatus { Configured = !string.IsNullOrEmpty(ResolveKey(providerId)) },
            StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyDictionary<string, ProviderCredentialStatus>>(result);
    }

    public Task SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct)
    {
        var envVarName = GetEnvVarName(providerId);
        _envFileValues[envVarName] = apiKey;
        EnvFileLoader.Save(_envFilePath, _envFileValues);
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string?> GetApiKeyAsync(string providerId, CancellationToken ct) => Task.FromResult(ResolveKey(providerId));

    private string? ResolveKey(string providerId)
    {
        foreach (var envVarName in GetEnvVarNames(providerId))
        {
            var fromRealEnv = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(fromRealEnv))
            {
                return fromRealEnv;
            }

            if (_envFileValues.TryGetValue(envVarName, out var fromFile) && !string.IsNullOrEmpty(fromFile))
            {
                return fromFile;
            }
        }

        return null;
    }

    private static string GetEnvVarName(string providerId) => GetEnvVarNames(providerId)[0];

    private static string[] GetEnvVarNames(string providerId) =>
        ProviderEnvVarNames.TryGetValue(providerId, out var names)
            ? names
            : throw new ArgumentException($"Unknown provider id '{providerId}'.", nameof(providerId));
}
