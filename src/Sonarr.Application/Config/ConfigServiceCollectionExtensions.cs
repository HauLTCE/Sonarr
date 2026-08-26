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
    /// <remarks>
    /// Needs an <see cref="IGuildDirectory"/> from somewhere, which <c>AddSonarrDiscord</c>
    /// registers. Order between the two calls does not matter — resolution happens per request — but
    /// dropping that registration would leave every config write throwing at the first
    /// <c>/config set</c> rather than at startup. The config service uses it to check that a
    /// snowflake is the kind of thing its key asks for; see <c>GuildConfigService.Missing</c>.
    /// </remarks>
    public static IServiceCollection AddSonarrConfig(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IGuildConfigService, GuildConfigService>();
        services.AddScoped<IFeatureGate, FeatureGate>();

        return services;
    }
}
