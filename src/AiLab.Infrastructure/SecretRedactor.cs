using System.Text.RegularExpressions;

namespace AiLab.Infrastructure;

/// <summary>
/// Strips API keys out of anything persisted/logged (raw requests/responses, snapshots, error
/// messages) — ported from the WinForms SecretRedactor, extended with an Anthropic key pattern.
/// </summary>
public static partial class SecretRedactor
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        text = OpenAiKeyPattern().Replace(text, "[REDACTED]");
        text = AnthropicKeyPattern().Replace(text, "[REDACTED]");
        text = BearerHeaderPattern().Replace(text, "Bearer [REDACTED]");
        return text;
    }

    [GeneratedRegex("sk-[A-Za-z0-9_-]{10,}")]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex("sk-ant-[A-Za-z0-9_-]{10,}")]
    private static partial Regex AnthropicKeyPattern();

    [GeneratedRegex("Bearer\\s+[A-Za-z0-9._-]{10,}")]
    private static partial Regex BearerHeaderPattern();
}
