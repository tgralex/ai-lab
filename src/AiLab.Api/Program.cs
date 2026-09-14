using System.Diagnostics;
using AiLab.Api.Endpoints;
using AiLab.Infrastructure.Credentials;
using AiLab.Infrastructure.DependencyInjection;
using AiLab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// `ailab configure` (or `dotnet run -- configure`): run the masked credential-entry loop and exit,
// without starting Kestrel. Lets a developer set up keys without touching the browser.
if (args.Length > 0 && args[0].Equals("configure", StringComparison.OrdinalIgnoreCase))
{
    var dataDirectory = DataPaths.ResolveDataDirectory(Directory.GetCurrentDirectory());
    DataPaths.EnsureDirectoriesExist(dataDirectory);
    var store = new EnvCredentialStore(dataDirectory);
    await ConsoleCredentialConfigurator.RunConfigureLoopAsync(store);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiLabInfrastructure(builder.Environment.ContentRootPath);

var port = ResolvePort(builder.Configuration, args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AiLabDbContext>();
    await db.Database.MigrateAsync();

    var credentialStore = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
    await ConsoleCredentialConfigurator.RunStartupPromptAsync(credentialStore);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/providers/status", async (ICredentialStore credentialStore, CancellationToken ct) =>
{
    var status = await credentialStore.GetAllStatusAsync(ct);
    var payload = status.ToDictionary(kvp => kvp.Key, kvp => new { configured = kvp.Value.Configured });
    return Results.Ok(payload);
});

app.MapWorkspaceEndpoints();
app.MapRequestEndpoints();
app.MapExecutionEndpoints();
app.MapModelCatalogEndpoints();
app.MapBenchmarkEndpoints();
app.MapComparisonEndpoints();
app.MapExportEndpoints();
app.MapExecutionPlanEndpoints();
app.MapAttachmentEndpoints();

// Explicit 404 for unmatched /api/* routes so they never fall through to the SPA's index.html.
app.MapMethods("/api/{**rest}", ["GET", "POST", "PUT", "DELETE", "PATCH"], () => Results.NotFound(new { error = "Not found" }));

var indexHtmlPath = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
if (File.Exists(indexHtmlPath))
{
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapFallback(() => Results.Content(
        "AI Lab API is running, but no Angular build was found in wwwroot.\n" +
        "Run 'npm run build' in src/AiLab.UI (or 'ng serve' for dev) and publish its output here.",
        "text/plain"));
}

var url = $"http://127.0.0.1:{port}";
app.Lifetime.ApplicationStarted.Register(() => TryOpenBrowser(url));

app.Run();

static int ResolvePort(IConfiguration configuration, string[] args)
{
    var envPort = Environment.GetEnvironmentVariable("AILAB_PORT");
    if (int.TryParse(envPort, out var fromEnv))
    {
        return fromEnv;
    }

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--port" && int.TryParse(args[i + 1], out var fromArg))
        {
            return fromArg;
        }
    }

    return configuration.GetValue("Server:Port", 8765);
}

static void TryOpenBrowser(string url)
{
    try
    {
        var psi = new ProcessStartInfo(url) { UseShellExecute = true };
        Process.Start(psi);
    }
    catch
    {
        // Best-effort only — a dev without a default browser handler shouldn't crash the app.
    }
}
