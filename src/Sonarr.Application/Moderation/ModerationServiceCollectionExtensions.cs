using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Moderation;

/// <summary>DI wiring for the moderation services (not the Discord handlers — those live in Sonarr.Bot).</summary>
public static class ModerationServiceCollectionExtensions
{
    /// <summary>
    /// Scoped, matching the repositories they wrap: a slash command or one job iteration is the
    /// unit of work (same rule as <c>AddSonarrConfig</c>).
    /// </summary>
    public static IServiceCollection AddSonarrModerationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<IAntiSpamService, AntiSpamService>();

        return services;
    }
}
