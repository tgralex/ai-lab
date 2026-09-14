namespace OpenAiBench.Core.Domain;

public sealed class Experiment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Experiment";
    public List<string> Tags { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
    public int Order { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public RequestDefinition Request { get; set; } = new();
    public List<AttachedFileRef> Files { get; set; } = new();

    /// <summary>In-memory run history for this experiment, newest last.</summary>
    public List<ExecutionRun> Runs { get; set; } = new();

    public Experiment Clone(string? newName = null) => new()
    {
        Name = newName ?? Name + " (copy)",
        Tags = new List<string>(Tags),
        Notes = Notes,
        Order = Order,
        Request = Request.Clone(),
        Files = Files.Select(f => new AttachedFileRef
        {
            Id = f.Id,
            OriginalFileName = f.OriginalFileName,
            StoredRelativePath = f.StoredRelativePath,
            SizeBytes = f.SizeBytes,
            ContentType = f.ContentType,
            Sha256 = f.Sha256,
            AddedAt = f.AddedAt
        }).ToList(),
        Runs = new List<ExecutionRun>()
    };
}
