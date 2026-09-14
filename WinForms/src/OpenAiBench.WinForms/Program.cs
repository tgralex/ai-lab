using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAiBench.Infrastructure.DependencyInjection;

namespace OpenAiBench.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var appRootDirectory = AppContext.BaseDirectory;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(appRootDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddOpenAiBenchInfrastructure(appRootDirectory, configuration);
        services.AddSingleton<MainForm>();

        using var provider = services.BuildServiceProvider();

        var mainForm = provider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }
}
