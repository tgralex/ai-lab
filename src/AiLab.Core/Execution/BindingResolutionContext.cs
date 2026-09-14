using System.Text.Json;

namespace AiLab.Core.Execution;

public sealed class PriorRequestOutput
{
    public required string RawOutputText { get; init; }

    /// <summary>Parsed once if RawOutputText is valid JSON; null otherwise (json.path bindings against it then resolve to "").</summary>
    public JsonElement? ParsedJson { get; init; }
}

/// <summary>
/// Everything InputBindingResolver needs to substitute {{...}} expressions — gathered by the
/// caller (workspace vars from the DB, prior outputs from an in-progress plan run) so
/// AiLab.Core.Execution stays free of any I/O.
/// </summary>
public sealed class BindingResolutionContext
{
    public IReadOnlyDictionary<string, string> WorkspaceVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>Keyed by AiRequestDefinition.Name. Empty when executing a request standalone (outside a plan).</summary>
    public IReadOnlyDictionary<string, PriorRequestOutput> PriorRequestOutputs { get; init; } = new Dictionary<string, PriorRequestOutput>();
}
