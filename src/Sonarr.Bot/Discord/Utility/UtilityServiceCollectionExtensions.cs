using Sonarr.Application.Utility;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Persistence.Repositories.Social;
using Sonarr.Infrastructure.Persistence.Repositories.Stats;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// One call that wires the server-management &amp; utility slice: the reminder / announce / welcome
/// services, the stats repository, and the three background workers (WelcomeFlow,
/// CommandUsageCounter, PresenceSampler).
/// </summary>
public static class UtilityServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c>, <c>AddSonarrRedis</c> and <c>AddSonarrConfig</c> (the
    /// services read guild config and the sampler writes the presence cache), and before
    /// <c>AddSonarrJobs</c> so the reminder and announce handlers have their services.
    /// </summary>
    public static IServiceCollection AddSonarrUtility(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSonarrUtilityServices();

        // stats.* belongs to this slice, so it is registered here rather than in
        // AddSonarrPersistence — same scoped lifetime as every other repository.
        services.AddScoped<IStatsRepository, StatsRepository>();

        // social.capsule, for /capsule write and the job that opens it.
        services.AddScoped<ICapsuleRepository, CapsuleRepository>();

        // social.event + social.event_rsvp, for /event and the RSVP buttons.
        services.AddScoped<IEventRepository, EventRepository>();

        // social.ticket, for /ticket and its close button.
        services.AddScoped<ITicketRepository, TicketRepository>();

        services.AddSingleton<WelcomeFlow>();
        services.AddHostedService(sp => sp.GetRequiredService<WelcomeFlow>());

        services.AddHostedService<CommandUsageCounter>();
        services.AddHostedService<PresenceSampler>();

        return services;
    }
}
