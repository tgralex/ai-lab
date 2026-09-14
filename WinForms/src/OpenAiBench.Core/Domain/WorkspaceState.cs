namespace OpenAiBench.Core.Domain;

public sealed class WorkspaceState
{
    public int SchemaVersion { get; init; } = 1;
    public List<Guid> ExperimentOrder { get; set; } = new();
    public string? WindowLayoutJson { get; set; }
}
