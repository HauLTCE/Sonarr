using Sonarr.Application.Levels;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Levels;
using Sonarr.Infrastructure.Persistence.Repositories.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// One call that wires the whole levels slice: the service, its three repositories, the voice
/// session tracker, and the three background pieces (message XP, voice accrual, activity flush).
/// </summary>
public static class LevelsServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c>, <c>AddSonarrRedis</c> and <c>AddSonarrConfig</c> (the
    /// level service reads guild policy through <c>IGuildConfigService</c> and the XP cooldown
    /// through <c>ICooldownStore</c>) and before <c>AddSonarrDiscord</c>.
    /// </summary>
    public static IServiceCollection AddSonarrLevels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The levels schema lives in this slice, so its repositories are registered here rather
        // than in AddSonarrPersistence — same scoped lifetime as every other repository.
        services.AddScoped<ILevelProgressRepository, LevelProgressRepository>();
        services.AddScoped<ILevelRewardRepository, LevelRewardRepository>();
        services.AddScoped<ISeasonRepository, SeasonRepository>();

        services.AddScoped<ILevelService, LevelService>();
        services.AddScoped<IVoiceSessionTracker, VoiceSessionTracker>();

        // Singleton: the buffer is the shared handoff between the message handler and the flusher.
        services.AddSingleton<ActivityBuffer>();

        services.AddSingleton<XpOnMessage>();
        services.AddHostedService(sp => sp.GetRequiredService<XpOnMessage>());

        services.AddSingleton<LevelsVoiceStateHandler>();
        services.AddHostedService(sp => sp.GetRequiredService<LevelsVoiceStateHandler>());

        services.AddHostedService<VoiceXpAccrual>();
        services.AddHostedService<ActivityFlusher>();

        return services;
    }
}
