using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAiBench.Core.Cost;
using OpenAiBench.Core.Execution;
using OpenAiBench.Core.Ports;
using OpenAiBench.Infrastructure.Configuration;
using OpenAiBench.Infrastructure.FileContent;
using OpenAiBench.Infrastructure.Logging;
using OpenAiBench.Infrastructure.OpenAi;
using OpenAiBench.Infrastructure.Persistence;
using OpenAiBench.Infrastructure.Pricing;

namespace OpenAiBench.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenAiBenchInfrastructure(
        this IServiceCollection services,
        string appRootDirectory,
        IConfiguration configuration)
    {
        var appSettings = new AppSettings();
        configuration.Bind(appSettings);
        services.AddSingleton(appSettings);

        var apiKeyProvider = new ApiKeyProvider(appRootDirectory);
        services.AddSingleton(apiKeyProvider);

        var workspacePath = ResolvePath(appRootDirectory, appSettings.WorkspacePath);
        var modelsPath = ResolvePath(appRootDirectory, appSettings.ModelsFile);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(appSettings.VerboseLogging ? LogLevel.Debug : LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(Path.Combine(workspacePath, "logs")));
        });

        var modelCatalog = new ModelCatalog(modelsPath);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IModelCapabilityProvider>(modelCatalog);
        services.AddSingleton<IPricingProvider>(modelCatalog);
        services.AddSingleton<ICostCalculator, CostCalculator>();
        services.AddSingleton<IWorkspaceStore>(new FileWorkspaceStore(workspacePath));
        services.AddSingleton<IFileContentExtractor, TextFileContentExtractor>();
        services.AddSingleton<IFileContentExtractor, DocxFileContentExtractor>();

        services.AddHttpClient<IOpenAiExperimentClient, OpenAiResponsesClient>((_, client) =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/v1/");
            var key = apiKeyProvider.HasApiKey ? apiKeyProvider.GetApiKeyOrThrow() : null;
            if (key is not null)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            // Streaming responses can legitimately run long; cancellation is via CancellationToken, not a client-wide timeout.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddSingleton<IExperimentRunner, ExperimentRunner>();
        services.AddSingleton<ExecuteAllCoordinator>();

        return services;
    }

    private static string ResolvePath(string appRootDirectory, string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.GetFullPath(Path.Combine(appRootDirectory, configuredPath));
}
