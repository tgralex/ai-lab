using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiLab.Core.Execution;

public sealed class BindingResolutionResult
{
    public required string ResolvedText { get; init; }

    /// <summary>Raw expression -> resolved value, for the RequestSnapshot's inspectable binding trail.</summary>
    public required IReadOnlyDictionary<string, string> ResolvedBindings { get; init; }

    public required IReadOnlyList<string> UnresolvedExpressions { get; init; }
}

/// <summary>
/// Resolves {{workspace.var}}, {{RequestName.output}}, and {{RequestName.json.path}} expressions
/// in prompt text. Explicit and inspectable by design — every resolved value is reported back in
/// ResolvedBindings rather than substituted silently with no trace.
/// </summary>
public static partial class InputBindingResolver
{
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex BindingPattern();

    public static BindingResolutionResult Resolve(string? text, BindingResolutionContext context)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new BindingResolutionResult
            {
                ResolvedText = text ?? string.Empty,
                ResolvedBindings = new Dictionary<string, string>(),
                UnresolvedExpressions = [],
            };
        }

        var resolvedBindings = new Dictionary<string, string>();
        var unresolved = new List<string>();

        var resolvedText = BindingPattern().Replace(text, match =>
        {
            var expression = match.Groups[1].Value.Trim();
            var value = ResolveExpression(expression, context);

            if (value is null)
            {
                unresolved.Add(expression);
                return match.Value; // leave the literal {{...}} in place so the gap is visible, not silently blanked
            }

            resolvedBindings[expression] = value;
            return value;
        });

        return new BindingResolutionResult
        {
            ResolvedText = resolvedText,
            ResolvedBindings = resolvedBindings,
            UnresolvedExpressions = unresolved,
        };
    }

    private static string? ResolveExpression(string expression, BindingResolutionContext context)
    {
        var parts = expression.Split('.', 2);
        if (parts.Length < 2)
        {
            return null;
        }

        var head = parts[0];
        var rest = parts[1];

        if (head == "workspace")
        {
            return context.WorkspaceVariables.TryGetValue(rest, out var value) ? value : null;
        }

        if (rest == "output")
        {
            return context.PriorRequestOutputs.TryGetValue(head, out var output) ? output.RawOutputText : null;
        }

        if (rest.StartsWith("json.", StringComparison.Ordinal))
        {
            if (!context.PriorRequestOutputs.TryGetValue(head, out var output) || output.ParsedJson is not { } json)
            {
                return null;
            }

            return NavigateJsonPath(json, rest["json.".Length..]);
        }

        return null;
    }

    private static string? NavigateJsonPath(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in ParsePathSegments(path))
        {
            if (segment.PropertyName is { } propertyName)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(propertyName, out var next))
                {
                    return null;
                }

                current = next;
            }
            else if (segment.ArrayIndex is { } index)
            {
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            _ => current.GetRawText(),
        };
    }

    private static IEnumerable<PathSegment> ParsePathSegments(string path)
    {
        foreach (var rawSegment in path.Split('.'))
        {
            var bracketIndex = rawSegment.IndexOf('[');
            if (bracketIndex < 0)
            {
                yield return new PathSegment { PropertyName = rawSegment };
                continue;
            }

            var propertyName = rawSegment[..bracketIndex];
            if (propertyName.Length > 0)
            {
                yield return new PathSegment { PropertyName = propertyName };
            }

            var indexText = rawSegment[(bracketIndex + 1)..].TrimEnd(']');
            if (int.TryParse(indexText, out var index))
            {
                yield return new PathSegment { ArrayIndex = index };
            }
        }
    }

    private readonly struct PathSegment
    {
        public string? PropertyName { get; init; }

        public int? ArrayIndex { get; init; }
    }
}
