namespace OpenAiBench.Core.Domain;

public sealed class ContentSet
{
    public string Text { get; set; } = string.Empty;
    public List<string> FileIds { get; set; } = new();

    public ContentSet Clone() => new() { Text = Text, FileIds = new List<string>(FileIds) };
}
