using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Persistence.Repositories;

namespace Sonarr.Infrastructure.Persistence;

/// <summary>DI wiring for Postgres. The host calls <see cref="AddSonarrPersistence"/> once.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SonarrDbContext"/> (Npgsql + pgvector) and the repositories.
    /// </summary>
    /// <remarks>
    /// Scoped context, scoped repositories: a slash command or job iteration is the unit of
    /// work. Singletons that need data resolve a scope themselves rather than capturing one.
    /// </remarks>
    public static IServiceCollection AddSonarrPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<SonarrDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddScoped<IGuildRepository, GuildRepository>();
        services.AddScoped<IGuildConfigRepository, GuildConfigRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();
        services.AddScoped<IJobRepository, JobRepository>();

        return services;
    }
}
