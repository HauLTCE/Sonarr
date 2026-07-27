using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Sonarr.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> run against this project alone, without booting
/// the bot host. Connection string comes from <c>PG_CONNECTION</c>; the localhost
/// fallback exists only so the CLI can build a model (no DB needed for scaffolding).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SonarrDbContext>
{
    private const string DevFallback =
        "Host=localhost;Port=5432;Database=sonarr;Username=sonarr;Password=sonarr";

    public SonarrDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION") is { Length: > 0 } env
            ? env
            : DevFallback;

        NpgsqlDataSourceBuilder dataSourceBuilder = new(connectionString);
        dataSourceBuilder.UseVector();

        DbContextOptionsBuilder<SonarrDbContext> options = new();
        options.UseNpgsql(dataSourceBuilder.Build(), npgsql => npgsql.UseVector());

        return new SonarrDbContext(options.Options);
    }
}
