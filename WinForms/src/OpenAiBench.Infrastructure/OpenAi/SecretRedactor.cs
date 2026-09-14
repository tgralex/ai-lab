using System.Text.RegularExpressions;

namespace OpenAiBench.Infrastructure.OpenAi;

/// <summary>Belt-and-braces redaction before any request/response diagnostic text is persisted or logged — the API key
/// should never appear in a body since it's only ever sent as an Authorization header, but this guards against it anyway.</summary>
public static partial class SecretRedactor
{
    public static string Redact(string text) => ApiKeyPattern().Replace(text, "[REDACTED]");

    [GeneratedRegex("sk-[A-Za-z0-9_-]{10,}")]
    private static partial Regex ApiKeyPattern();
}
