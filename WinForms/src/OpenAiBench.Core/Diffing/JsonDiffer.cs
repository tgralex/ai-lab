using System.Text.Json;

namespace OpenAiBench.Core.Diffing;

public static class JsonDiffer
{
    /// <summary>Throws <see cref="JsonException"/> if either input is not valid JSON — callers should fall back to <see cref="TextDiffer"/> in that case.</summary>
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
            differences.Add(new JsonDiffEntry { Path = path, Kind = JsonDiffKind.Changed, LeftValue = Render(left), RightValue = Render(right) });
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
                if (Render(left) != Render(right))
                {
                    differences.Add(new JsonDiffEntry { Path = path, Kind = JsonDiffKind.Changed, LeftValue = Render(left), RightValue = Render(right) });
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
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Removed, LeftValue = Render(leftValue) });
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
                differences.Add(new JsonDiffEntry { Path = $"{path}.{name}", Kind = JsonDiffKind.Added, RightValue = Render(rightValue) });
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
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Added, RightValue = Render(rightItems[i]) });
            }
            else if (i >= rightItems.Length)
            {
                differences.Add(new JsonDiffEntry { Path = childPath, Kind = JsonDiffKind.Removed, LeftValue = Render(leftItems[i]) });
            }
            else
            {
                Compare(leftItems[i], rightItems[i], childPath, differences);
            }
        }
    }

    private static string Render(JsonElement element) => element.ToString();
}
