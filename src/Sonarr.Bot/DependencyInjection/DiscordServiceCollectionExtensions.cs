using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Utility;
using Sonarr.Bot.Observability;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.DependencyInjection;

/// <summary>
/// Gateway + interaction framework registration. Every interface is registered once,
/// here or in a sibling extension (docs/02-architecture.md#cross-cutting).
/// </summary>
public static class DiscordServiceCollectionExtensions
{
    public static IServiceCollection AddSonarrDiscord(this IServiceCollection services)
    {
        services.AddSingleton(new DiscordSocketConfig
        {
            // GuildMembers/MessageContent are privileged and must be enabled in the
            // developer portal. Both are load-bearing: levels and welcome need members,
            // the chat engine needs content (docs/08-background-services.md).
            //
            // Invites and scheduled events are subtracted rather than opted into one by one:
            // AllUnprivileged is a moving bundle and re-listing it by hand would silently miss
            // whatever Discord adds next. Nothing subscribes to either — /event creates the
            // native event over REST, which needs no intent — and Discord.Net says so at
            // startup via LogGatewayIntentWarnings. Dropping them is two fewer dispatch streams
            // to decode on the J2900 and, more usefully, a log with no standing warnings in it,
            // so a new one is visible. Add an intent back the same day you add a handler for it.
            GatewayIntents = (GatewayIntents.AllUnprivileged
                              & ~(GatewayIntents.GuildInvites | GatewayIntents.GuildScheduledEvents))
                             | GatewayIntents.GuildMembers
                             | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = true,
            LogGatewayIntentWarnings = true,
            MessageCacheSize = 100,
        });

        services.AddSingleton<DiscordSocketClient>();
        services.AddSingleton<IDiscordClient>(sp => sp.GetRequiredService<DiscordSocketClient>());

        services.AddSingleton(new InteractionServiceConfig
        {
            DefaultRunMode = RunMode.Async,
            UseCompiledLambda = true,
        });
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            sp.GetRequiredService<InteractionServiceConfig>()));

        // Channel/role/member names off the gateway cache. Used by GuildConfigService to check a
        // snowflake is the kind of thing its key asks for, so losing the registration makes every
        // `/config set` throw at resolution.
        services.AddSingleton<IGuildDirectory, GatewayGuildDirectory>();

        // The error pipeline's user-facing half: the same friendly line the user saw, kept per user
        // so `sonarr errors` has something to show (docs/07).
        services.AddSingleton<UserErrorLog>();

        services.AddHostedService<InteractionHandler>();
        services.AddHostedService<DiscordGatewayService>();

        // Self-test probes: the aggregator itself comes from AddSonarrHealth. Each probe owns a
        // connection this project holds, which is why they live here and not in Application
        // (docs/02-architecture.md, docs/08-background-services.md).
        services.AddSingleton<ISelfTestProbe, PostgresProbe>();
        services.AddSingleton<ISelfTestProbe, RedisProbe>();
        services.AddSingleton<ISelfTestProbe, LavalinkProbe>();
        services.AddSingleton<ISelfTestProbe, DiscordPermissionsProbe>();
        services.AddHostedService<SelfTestHostedService>();

        return services;
    }
}
