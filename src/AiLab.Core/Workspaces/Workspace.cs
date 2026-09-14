namespace AiLab.Core.Workspaces;

public sealed class Workspace
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? Description { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkspaceVariable
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid WorkspaceId { get; init; }

    public required string Name { get; set; }

    public string Value { get; set; } = string.Empty;
}
