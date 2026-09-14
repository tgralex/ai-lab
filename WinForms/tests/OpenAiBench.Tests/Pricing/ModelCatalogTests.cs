using OpenAiBench.Infrastructure.Pricing;
using Xunit;

namespace OpenAiBench.Tests.Pricing;

public class ModelCatalogTests : IDisposable
{
    private readonly string _modelsFilePath = Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid()}.json");

    private const string SampleJson = """
        [
          { "id": "gpt-test-flagship", "displayName": "Test Flagship", "recommended": true, "supportsReasoningEffort": true, "supportsStructuredOutput": true, "supportsStreaming": true, "supportsPromptCacheKey": true, "maxOutputTokensLimit": 128000, "inputPerMillion": 2.00, "cachedInputPerMillion": 0.20, "outputPerMillion": 12.00 },
          { "id": "gpt-test-legacy", "displayName": "Test Legacy", "recommended": false, "supportsReasoningEffort": false, "supportsStructuredOutput": true, "supportsStreaming": true, "supportsPromptCacheKey": false, "maxOutputTokensLimit": 16384, "inputPerMillion": 0.50, "cachedInputPerMillion": 0.25, "outputPerMillion": 1.50 }
        ]
        """;

    public ModelCatalogTests()
    {
        File.WriteAllText(_modelsFilePath, SampleJson);
    }

    [Fact]
    public void GetAll_PreservesFileOrder()
    {
        var catalog = new ModelCatalog(_modelsFilePath);

        var ids = catalog.GetAll().Select(m => m.Id).ToList();

        Assert.Equal(new[] { "gpt-test-flagship", "gpt-test-legacy" }, ids);
    }

    [Fact]
    public void Get_KnownModel_ReturnsCapabilityFlags()
    {
        var catalog = new ModelCatalog(_modelsFilePath);

        var model = catalog.Get("gpt-test-flagship");

        Assert.True(model.Recommended);
        Assert.True(model.SupportsReasoningEffort);
        Assert.Equal(128000, model.MaxOutputTokensLimit);
    }

    [Fact]
    public void Get_UnknownModel_ReturnsConservativeDefaults()
    {
        var catalog = new ModelCatalog(_modelsFilePath);

        var model = catalog.Get("some-future-model-id");

        Assert.False(model.SupportsReasoningEffort);
        Assert.False(model.SupportsStructuredOutput);
        Assert.False(model.SupportsPromptCacheKey);
    }

    [Fact]
    public void TryGet_KnownModel_ReturnsMatchingPricing()
    {
        var catalog = new ModelCatalog(_modelsFilePath);

        var pricing = catalog.TryGet("gpt-test-legacy");

        Assert.NotNull(pricing);
        Assert.Equal(0.50m, pricing!.InputPerMillion);
        Assert.Equal(0.25m, pricing.CachedInputPerMillion);
        Assert.Equal(1.50m, pricing.OutputPerMillion);
    }

    [Fact]
    public void TryGet_UnknownModel_ReturnsNull()
    {
        var catalog = new ModelCatalog(_modelsFilePath);

        Assert.Null(catalog.TryGet("does-not-exist"));
    }

    [Fact]
    public void Constructor_MissingFile_ProducesEmptyCatalogRatherThanThrowing()
    {
        var catalog = new ModelCatalog(Path.Combine(Path.GetTempPath(), "does-not-exist.json"));

        Assert.Empty(catalog.GetAll());
    }

    public void Dispose()
    {
        if (File.Exists(_modelsFilePath))
        {
            File.Delete(_modelsFilePath);
        }
    }
}
