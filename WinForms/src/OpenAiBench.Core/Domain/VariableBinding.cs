namespace OpenAiBench.Core.Domain;

public enum VariableBindingKind
{
    Text,
    File
}

public sealed class VariableBinding
{
    public required string Name { get; init; }
    public VariableBindingKind Kind { get; init; }
    public string? TextValue { get; init; }
    public string? FileId { get; init; }
}
