namespace AiLab.Infrastructure.Credentials;

/// <summary>
/// Prints provider credential status at startup and offers an interactive masked-input flow to
/// configure missing keys, e.g.:
///   OpenAI: configured
///   Anthropic: missing
///   Press C to configure provider credentials, any other key to continue...
/// Never echoes the plaintext key — reads char-by-char and prints '*' per character.
/// </summary>
public static class ConsoleCredentialConfigurator
{
    public static async Task RunStartupPromptAsync(ICredentialStore credentialStore, CancellationToken ct = default)
    {
        if (Console.IsInputRedirected)
        {
            // Non-interactive host (CI, service) — skip the prompt entirely.
            return;
        }

        var status = await credentialStore.GetAllStatusAsync(ct);
        foreach (var (providerId, providerStatus) in status)
        {
            Console.WriteLine($"{Capitalize(providerId)}: {(providerStatus.Configured ? "configured" : "missing")}");
        }

        Console.WriteLine("Press C to configure provider credentials, any other key to continue...");
        var key = SafeReadKey();
        if (key is 'c' or 'C')
        {
            await RunConfigureLoopAsync(credentialStore, ct);
        }
    }

    public static async Task RunConfigureLoopAsync(ICredentialStore credentialStore, CancellationToken ct = default)
    {
        var status = await credentialStore.GetAllStatusAsync(ct);
        var providerIds = status.Keys.ToList();

        foreach (var providerId in providerIds)
        {
            Console.Write($"Enter API key for {Capitalize(providerId)} (blank to skip): ");
            var key = ReadMaskedLine();
            Console.WriteLine();

            if (!string.IsNullOrWhiteSpace(key))
            {
                await credentialStore.SetApiKeyAsync(providerId, key, ct);
                Console.WriteLine($"{Capitalize(providerId)}: configured");
            }
        }
    }

    private static string ReadMaskedLine()
    {
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var info = Console.ReadKey(intercept: true);
            if (info.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (info.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(info.KeyChar))
            {
                buffer.Append(info.KeyChar);
                Console.Write('*');
            }
        }

        return buffer.ToString();
    }

    private static char? SafeReadKey()
    {
        try
        {
            return Console.ReadKey(intercept: true).KeyChar;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
