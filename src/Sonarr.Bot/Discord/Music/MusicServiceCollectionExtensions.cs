using Lavalink4NET.Extensions;
using Sonarr.Application.Music;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;
using Sonarr.Infrastructure.Persistence.Repositories.Music;

namespace Sonarr.Bot.Discord.Music;

/// <summary>
/// One call that wires the whole music slice: the Lavalink client, the service, its three
/// repositories, the voice-move handling and the session snapshotter.
/// </summary>
/// <remarks>
/// <b>Client only.</b> The Lavalink 4.2.2 node runs on the host and is not ours to manage — this
/// registers a client that connects to it (docs/03-stack.md).
/// </remarks>
public static class MusicServiceCollectionExtensions
{
    /// <summary>
    /// Add after <c>AddSonarrPersistence</c>, <c>AddSonarrRedis</c> and <c>AddSonarrConfig</c>
    /// (the service reads guild policy and the session cache) and before <c>AddSonarrDiscord</c>.
    /// </summary>
    public static IServiceCollection AddSonarrMusic(this IServiceCollection services, SonarrOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Lavalink4NET's Discord.Net integration brings its own IDiscordClientWrapper over the
        // DiscordSocketClient registered by AddSonarrDiscord.
        services.AddLavalink();
        services.ConfigureLavalink(lavalink =>
        {
            lavalink.BaseAddress = new Uri(options.LavalinkUri);
            lavalink.Passphrase = options.LavalinkPassword;
            lavalink.Label = "sonarr";
        });

        // The music schema lives in this slice, so its repositories are registered here rather
        // than in AddSonarrPersistence — same scoped lifetime as every other repository.
        services.AddScoped<IPlaylistRepository, PlaylistRepository>();
        services.AddScoped<IMusicStatsRepository, MusicStatsRepository>();
        services.AddScoped<IMusicPrefsRepository, MusicPrefsRepository>();

        services.AddScoped<IMusicService, MusicService>();

        // Singletons: the gateway holds the per-guild idle-disconnect timers, and the watcher is a
        // gateway event handler — both outlive any one interaction scope.
        services.AddSingleton<IVoicePlayerGateway, VoicePlayerGateway>();
        services.AddSingleton<VoiceMoveCoordinator>();
        services.AddSingleton<NowPlayingPresenter>();

        services.AddHostedService<VoiceStateWatcher>();
        services.AddHostedService<MusicSessionSnapshotter>();
        services.AddHostedService<TrackEventRelay>();

        return services;
    }
}
