using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiLab.Infrastructure.Persistence;

/// <summary>Design-time factory so `dotnet ef migrations add` works without running the full API host.</summary>
public sealed class AiLabDbContextFactory : IDesignTimeDbContextFactory<AiLabDbContext>
{
    public AiLabDbContext CreateDbContext(string[] args)
    {
        var dataDirectory = DataPaths.ResolveDataDirectory();
        DataPaths.EnsureDirectoriesExist(dataDirectory);

        var optionsBuilder = new DbContextOptionsBuilder<AiLabDbContext>();
        optionsBuilder.UseSqlite($"Data Source={DataPaths.GetDatabasePath(dataDirectory)}");

        return new AiLabDbContext(optionsBuilder.Options);
    }
}
