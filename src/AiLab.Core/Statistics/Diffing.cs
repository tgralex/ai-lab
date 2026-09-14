using System.Text.Json;

namespace AiLab.Core.Statistics;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,
}

public sealed class DiffLine
{
    public required DiffLineKind Kind { get; init; }

    public required string Text { get; init; }
}

public sealed class TextDiffResult
{
    public required IReadOnlyList<DiffLine> Lines { get; init; }

    public bool IsEqual => Lines.All(l => l.Kind == DiffLineKind.Unchanged);
}

/// <summary>Classic LCS-based line diff — ported from the WinForms TextDiffer.</summary>
public static class TextDiffer
{
    public static TextDiffResult Diff(string left, string right)
    {
        var leftLines = left.Replace("\r\n", "\n").Split('\n');
        var rightLines = right.Replace("\r\n", "\n").Split('\n');

        var lcs = ComputeLcsTable(leftLines, rightLines);
        var lines = new List<DiffLine>();
        Backtrack(lcs, leftLines, rightLines, leftLines.Length, rightLines.Length, lines);
        lines.Reverse();

        return new TextDiffResult { Lines = lines };
    }

    private static int[,] ComputeLcsTable(string[] left, string[] right)
    {
        var table = new int[left.Length + 1, right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                table[i, j] = left[i - 1] == right[j - 1]
                    ? table[i - 1, j - 1] + 1
                    : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table;
    }

    private static void Backtrack(int[,] lcs, string[] left, string[] right, int i, int j, List<DiffLine> output)
    {
        while (true)
        {
            if (i > 0 && j > 0 && left[i - 1] == right[j - 1])
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Unchanged, Text = left[i - 1] });
                i--; j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Added, Text = right[j - 1] });
                j--;
            }
            else if (i > 0 && (j == 0 || lcs[i, j - 1] < lcs[i - 1, j]))
            {
                output.Add(new DiffLine { Kind = DiffLineKind.Removed, Text = left[i - 1] });
                i--;
            }
            else
            {
                break;
            }
        }
    }
}

public enum JsonDiffKind
{
    Added,
    Removed,
    Changed,
}

public sealed class JsonDiffEntry
{
    public required string Path { get; init; }

    public required JsonDiffKind Kind { get; init; }

    public string? LeftValue { get; init; }

    public string? RightValue { get; init; }
}

public sealed class JsonDiffResult
{
    public required IReadOnlyList<JsonDiffEntry> Differences { get; init; }

    public bool IsEqual => Differences.Count == 0;
}

/// <summary>Structural JSON diff — ported from the WinForms JsonDiffer. Throws JsonException if either side isn't valid JSON.</summary>
public static class JsonDiffer
{
    public static JsonDiffResult Diff(string left, string right)
    {
        using var leftDoc = JsonDocument.Parse(left);
        using var rightDoc = JsonDocument.Parse(right);

        var differences = new List<JsonDiffEntry>();
        Compare(leftDoc.RootElement, rightDoc.RootElement, "$", differences);

        return new JsonDiffResult { Differences = differences };
    }

    private static void Compare(JsonElement left, JsonElement right, string path, List<JsonDiffEntry> differences)
    {
        if (left.ValueKind != right.ValueKind)
        {
            differences.Add(new JsonDiffEntry { Path = path, Kind = JsonDiffKind.Changed, LeftValue = left.ToString(), RightValue = right.ToString() });
            return;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                CompareObjects(left, right, path, differences);
                break;
            case JsonValueKind.Array:
                CompareArrays(left, right, path, differences);
                break;
            default:
                if (left.ToString() != right.ToString())
                {
                    differences.Add(new JsonDiffEntry { Path = path, Kind = JsonDiffKind.Changed, LeftValue = left.ToString(), RightValue = right.ToString() });
                }
                break;
        }
    }

    private static void CompareObjects(JsonElement left, JsonElement right, string path, List<JsonDiffEntry> differences)
    {
        var leftProps = left.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        var rightProps = right.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

        foreach (var (name, leftValue) in leftProps)
        {
            var childPath = $"{path}.{name}";
            if (!rightProps.TryGetValue(name, out var rightValue))
            {
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Removed, LeftValue = leftValue.ToString() });
            }
            else
            {
                Compare(leftValue, rightValue, childPath, differences);
            }
        }

        foreach (var (name, rightValue) in rightProps)
        {
            if (!leftProps.ContainsKey(name))
            {
                differences.Add(new JsonDiffEntry { Path = $"{path}.{name}", Kind = JsonDiffKind.Added, RightValue = rightValue.ToString() });
            }
        }
    }

    private static void CompareArrays(JsonElement left, JsonElement right, string path, List<JsonDiffEntry> differences)
    {
        var leftItems = left.EnumerateArray().ToArray();
        var rightItems = right.EnumerateArray().ToArray();
        var max = Math.Max(leftItems.Length, rightItems.Length);

        for (var i = 0; i < max; i++)
        {
            var childPath = $"{path}[{i}]";
            if (i >= leftItems.Length)
            {
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Added, RightValue = rightItems[i].ToString() });
            }
            else if (i >= rightItems.Length)
            {
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Removed, LeftValue = leftItems[i].ToString() });
            }
            else
            {
                Compare(leftItems[i], rightItems[i], childPath, differences);
            }
        }
    }
}
