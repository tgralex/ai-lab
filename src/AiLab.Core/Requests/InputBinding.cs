namespace AiLab.Core.Requests;

/// <summary>
/// An explicit, inspectable reference embedded in prompt text, e.g. {{workspace.resume}},
/// {{ParseResume.output}}, {{ParseJob.json.requiredSkills}}. Resolved by AiLab.Core.Execution
/// before the provider ever sees the request — never resolved implicitly/"magically".
/// </summary>
public sealed class InputBinding
{
    /// <summary>The raw expression as it appears between {{ }}, e.g. "ParseResume.json.skills".</summary>
    public required string Expression { get; init; }

    public InputBindingKind Kind { get; init; }

    /// <summary>For Kind == PriorRequestOutput / PriorRequestJsonPath: the referenced request's name.</summary>
    public string? SourceRequestName { get; init; }

    /// <summary>For Kind == PriorRequestJsonPath: the JSON path into that request's parsed output.</summary>
    public string? JsonPath { get; init; }

    /// <summary>For Kind == WorkspaceVariable: the variable name.</summary>
    public string? VariableName { get; init; }
}

public enum InputBindingKind
{
    WorkspaceVariable,
    PriorRequestOutput,
    PriorRequestJsonPath,
}
