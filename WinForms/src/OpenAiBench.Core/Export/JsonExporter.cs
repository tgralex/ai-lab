using System.Text.Json;

namespace OpenAiBench.Core.Export;

public static class JsonExporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Export(IEnumerable<BenchmarkExportRow> rows) =>
        JsonSerializer.Serialize(rows.ToList(), Options);
}
