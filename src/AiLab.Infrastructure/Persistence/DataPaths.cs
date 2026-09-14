namespace AiLab.Infrastructure.Persistence;

/// <summary>Resolves the app's data directory (db, attachments, logs) from AILAB_DATA_DIR or a sensible default.</summary>
public static class DataPaths
{
    public static string ResolveDataDirectory(string? baseDirectoryOverride = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable("AILAB_DATA_DIR");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        var baseDir = baseDirectoryOverride ?? AppContext.BaseDirectory;
        return Path.Combine(baseDir, "data");
    }

    public static string GetDatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "ailab.db");

    public static string GetAttachmentsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "attachments");

    public static string GetLogsDirectory(string dataDirectory) => Path.Combine(dataDirectory, "logs");

    public static void EnsureDirectoriesExist(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(GetAttachmentsDirectory(dataDirectory));
        Directory.CreateDirectory(GetLogsDirectory(dataDirectory));
    }
}
