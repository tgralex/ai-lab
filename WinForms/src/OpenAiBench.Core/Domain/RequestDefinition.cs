namespace OpenAiBench.Core.Domain;

public sealed class RequestDefinition
{
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;

    public ContentSet CachedContext { get; set; } = new();
    public ContentSet UserContext { get; set; } = new();

    public List<VariableBinding> Variables { get; set; } = new();

    public string? ResponseSchema { get; set; }

    public string? ReasoningEffort { get; set; }
    public int? MaxOutputTokens { get; set; }

    public bool Stream { get; set; } = true;

    public string? PromptCacheKey { get; set; }

    public Dictionary<string, string> AdditionalSettings { get; set; } = new();

    public RequestDefinition Clone() => new()
    {
        Model = Model,
        SystemPrompt = SystemPrompt,
        CachedContext = CachedContext.Clone(),
        UserContext = UserContext.Clone(),
        Variables = Variables.Select(v => new VariableBinding { Name = v.Name, Kind = v.Kind, TextValue = v.TextValue, FileId = v.FileId }).ToList(),
        ResponseSchema = ResponseSchema,
        ReasoningEffort = ReasoningEffort,
        MaxOutputTokens = MaxOutputTokens,
        Stream = Stream,
        PromptCacheKey = PromptCacheKey,
        AdditionalSettings = new Dictionary<string, string>(AdditionalSettings)
    };
}
