using AiLab.Core.Execution;
using AiLab.Core.ExecutionPlans;
using AiLab.Core.Providers;
using AiLab.Core.Statistics;
using AiLab.Infrastructure.Anthropic;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.Files;
using AiLab.Infrastructure.ModelCatalog;
using AiLab.Infrastructure.OpenAi;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAiLabInfrastructure(this IServiceCollection services, string? contentRootPath = null)
    {
        var dataDirectory = DataPaths.ResolveDataDirectory(contentRootPath);
        DataPaths.EnsureDirectoriesExist(dataDirectory);

        services.AddDbContext<AiLabDbContext>(options =>
            options.UseSqlite($"Data Source={DataPaths.GetDatabasePath(dataDirectory)}"));

        services.AddSingleton<ICredentialStore>(_ => new EnvCredentialStore(dataDirectory));
        services.AddSingleton(_ => new AttachmentStorage(dataDirectory));
        services.AddSingleton<IFileContentExtractor, TextFileContentExtractor>();
        services.AddSingleton<IFileContentExtractor, DocxFileContentExtractor>();
        services.AddScoped<IAttachmentContentProvider, AttachmentContentProvider>();

        // Streaming responses can run long — no client-side timeout; CancellationToken governs lifetime instead.
        // Each provider gets its own typed-client registration (keyed by its concrete type, not the
        // shared IAiProvider interface) — otherwise AddHttpClient<TClient,...> keys its named client
        // off TClient and the two providers' BaseAddress configurations collide.
        services.AddHttpClient<OpenAiProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient<AnthropicProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        // Scoped (not Singleton) so each request scope re-resolves via IHttpClientFactory, avoiding
        // a captive-dependency handler that never rotates.
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<AnthropicProvider>());

        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddScoped<AiRequestExecutor>();
        services.AddScoped<ExecutionPlanEngine>();
        services.AddScoped<IProviderModelCatalogService, ProviderModelCatalogService>();

        return services;
    }
}
