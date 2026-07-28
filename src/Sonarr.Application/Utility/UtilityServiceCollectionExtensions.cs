using Microsoft.Extensions.DependencyInjection;
using Sonarr.Application.Reminders;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Utility;

/// <summary>DI wiring for reminders, announcements and the welcome flow's decisions.</summary>
public static class UtilityServiceCollectionExtensions
{
    /// <summary>
    /// Scoped, matching the repositories they wrap — a slash command or one job iteration is the
    /// unit of work (same rule as <c>AddSonarrConfig</c>).
    /// </summary>
    public static IServiceCollection AddSonarrUtilityServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ZoneResolver>();

        // /ship. Stateless and singleton-safe, but scoped like its neighbours: it holds nothing
        // between calls and the PersonaHolder it reads is the singleton either way.
        services.AddScoped<ShipMeter>();

        // /capsule write. The repository is registered by the social slice's wiring.
        services.AddScoped<ICapsuleService, CapsuleService>();

        // /event. Same reason for the interface: it takes the internal ZoneResolver.
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IReminderService, ReminderService>();
        services.AddScoped<IAnnounceService, AnnounceService>();
        services.AddScoped<IWelcomeService, WelcomeService>();

        return services;
    }
}
