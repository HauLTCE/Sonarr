using Sonarr.Application.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Persistence.Repositories.Mod;

namespace Sonarr.Bot.Discord.Moderation;

/// <summary>
/// One call that wires the whole moderation slice: the services, the case repository, the
/// AntiSpam gateway handler and the tempban-lift job handler.
/// </summary>
public static class ModerationServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c> and <c>AddSonarrConfig</c> (the moderation service
    /// reads guild policy through <c>IGuildConfigService</c>) and before <c>AddSonarrDiscord</c>.
    /// </summary>
    public static IServiceCollection AddSonarrModeration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSonarrModerationServices();

        // mod.case lives in the moderation slice, so it is registered here rather than in
        // AddSonarrPersistence — same scoped lifetime as every other repository.
        services.AddScoped<IModCaseRepository, ModCaseRepository>();

        // Scoped: JobScheduler resolves IJobHandler inside its per-tick scope, so this handler can
        // depend on the scoped IModCaseRepository above.
        services.AddScoped<IJobHandler, TempBanLiftJobHandler>();

        services.AddSingleton<AntiSpamHandler>();
        services.AddHostedService(sp => sp.GetRequiredService<AntiSpamHandler>());

        return services;
    }
}
