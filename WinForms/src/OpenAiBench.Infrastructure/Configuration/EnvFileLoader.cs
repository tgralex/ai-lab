namespace OpenAiBench.Infrastructure.Configuration;

/// <summary>Minimal .env parser: KEY=value lines, optional surrounding quotes, '#' comments, blank lines ignored.</summary>
public static class EnvFileLoader
{
    public static IReadOnlyDictionary<string, string> Load(string envFilePath)
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(envFilePath))
        {
            return result;
        }

        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        return result;
    }
}
