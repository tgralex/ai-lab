namespace OpenAiBench.Infrastructure.Configuration;

public sealed class AppSettings
{
    public string WorkspacePath { get; set; } = "./Workspace";
    public string DefaultModel { get; set; } = "gpt-5.6-terra";
    public int MaxConcurrency { get; set; } = 4;
    public string ModelsFile { get; set; } = "./models.json";
    public bool VerboseLogging { get; set; }
}
