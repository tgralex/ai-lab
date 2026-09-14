namespace OpenAiBench.Infrastructure.Configuration;

/// <summary>
/// Resolves OPENAI_API_KEY. A real OS environment variable takes precedence over the value in
/// .env (the spec's "override"). The key is never persisted anywhere by this class and callers
/// must never log or persist the returned value.
/// </summary>
public sealed class ApiKeyProvider
{
    private const string EnvVarName = "OPENAI_API_KEY";
    private readonly string? _apiKey;

    public ApiKeyProvider(string appRootDirectory)
    {
        var realEnvValue = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(realEnvValue))
        {
            _apiKey = realEnvValue;
            return;
        }

        var envFilePath = Path.Combine(appRootDirectory, ".env");
        var values = EnvFileLoader.Load(envFilePath);
        _apiKey = values.TryGetValue(EnvVarName, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile)
            ? fromFile
            : null;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public string GetApiKeyOrThrow() =>
        _apiKey ?? throw new InvalidOperationException(
            "OPENAI_API_KEY is not set. Set it as a real environment variable or add it to .env at the app root.");
}
