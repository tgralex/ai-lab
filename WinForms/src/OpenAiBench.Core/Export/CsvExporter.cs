using System.Globalization;
using System.Text;

namespace OpenAiBench.Core.Export;

public static class CsvExporter
{
    private static readonly string[] Header =
    {
        "Experiment", "Run", "StartedAt", "Model", "Reasoning", "TotalMs", "TTFTMs", "GenerationMs",
        "InputTokens", "CachedTokens", "OutputTokens", "ReasoningTokens", "TokensPerSecond", "EstimatedCost", "Status"
    };

    public static string Export(IEnumerable<BenchmarkExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', Header));

        foreach (var row in rows)
        {
            var fields = new[]
            {
                Escape(row.Experiment),
                Escape(row.Run),
                row.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                Escape(row.Model ?? string.Empty),
                Escape(row.Reasoning ?? string.Empty),
                row.TotalMs.ToString(CultureInfo.InvariantCulture),
                row.TtftMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.GenerationMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.InputTokens.ToString(CultureInfo.InvariantCulture),
                row.CachedTokens.ToString(CultureInfo.InvariantCulture),
                row.OutputTokens.ToString(CultureInfo.InvariantCulture),
                row.ReasoningTokens.ToString(CultureInfo.InvariantCulture),
                row.TokensPerSecond?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.EstimatedCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Escape(row.Status)
            };
            builder.AppendLine(string.Join(',', fields));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
