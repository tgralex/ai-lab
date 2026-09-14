using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AiLab.Core.Statistics;

/// <summary>Flat row shape for CSV/JSON export — one row per ExecutionRun. Never carries API keys or raw secrets.</summary>
public sealed class BenchmarkExportRow
{
    public required string Workspace { get; init; }

    public string? ExecutionPlan { get; init; }

    public required string Request { get; init; }

    public required Guid Run { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public string? Reasoning { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public double? TotalMs { get; init; }

    public double? TtftMs { get; init; }

    public double? GenerationMs { get; init; }

    public int InputTokens { get; init; }

    public int CachedTokens { get; init; }

    public int OutputTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public double? TokensPerSecond { get; init; }

    public decimal? EstimatedCost { get; init; }

    public decimal? ActualCost { get; init; }

    public required string Status { get; init; }
}

public static class CsvExporter
{
    private static readonly string[] Header =
    [
        "Workspace", "ExecutionPlan", "Request", "Run", "Provider", "Model", "Reasoning",
        "StartedAt", "TotalMs", "TtftMs", "GenerationMs", "InputTokens", "CachedTokens",
        "OutputTokens", "ReasoningTokens", "TokensPerSecond", "EstimatedCost", "ActualCost", "Status",
    ];

    public static string Export(IReadOnlyList<BenchmarkExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));

        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.Workspace,
                row.ExecutionPlan ?? "",
                row.Request,
                row.Run.ToString(),
                row.Provider,
                row.Model,
                row.Reasoning ?? "",
                row.StartedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                row.TotalMs?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.TtftMs?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.GenerationMs?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.InputTokens.ToString(CultureInfo.InvariantCulture),
                row.CachedTokens.ToString(CultureInfo.InvariantCulture),
                row.OutputTokens.ToString(CultureInfo.InvariantCulture),
                row.ReasoningTokens.ToString(CultureInfo.InvariantCulture),
                row.TokensPerSecond?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.EstimatedCost?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.ActualCost?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.Status,
            };

            sb.AppendLine(string.Join(',', fields.Select(EscapeCsvField)));
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}

public static class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Export(IReadOnlyList<BenchmarkExportRow> rows) => JsonSerializer.Serialize(rows, Options);
}
