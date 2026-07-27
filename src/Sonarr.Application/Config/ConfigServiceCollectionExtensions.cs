using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Config;

/// <summary>DI wiring for the config system and kill switches.</summary>
public static class ConfigServiceCollectionExtensions
{
    /// <summary>
    /// Scoped, matching the repositories they wrap (<c>AddSonarrPersistence</c>) — a slash command
    /// is the unit of work.
    /// </summary>
    public static IServiceCollection AddSonarrConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IGuildConfigService, GuildConfigService>();
        services.AddScoped<IFeatureGate, FeatureGate>();

        return services;
    }
}
