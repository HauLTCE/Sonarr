using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Discord;
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
            GatewayIntents = GatewayIntents.AllUnprivileged
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

        // The error pipeline's panel half: the same friendly line the user saw, kept per user so
        // "My errors" has something to show (docs/09).
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
