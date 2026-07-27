using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Configuration;

namespace Sonarr.Bot.Discord;

/// <summary>
/// Owns the gateway connection lifetime and slash-command registration.
/// Registration is per-guild in dev (instant) and global in prod
/// (docs/07-commands.md#design-rules).
/// </summary>
public sealed class DiscordGatewayService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    SonarrOptions options,
    ILogger<DiscordGatewayService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += ForwardLog;
        interactions.Log += ForwardLog;
        client.Ready += OnReadyAsync;

        await interactions.AddModulesAsync(typeof(DiscordGatewayService).Assembly, services);

        await client.LoginAsync(TokenType.Bot, options.DiscordToken);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private async Task OnReadyAsync()
    {
        if (options.DiscordDevGuildId is { } devGuild)
        {
            await interactions.RegisterCommandsToGuildAsync(devGuild);
            log.LogInformation("Commands registered to dev guild {GuildId}", devGuild);
        }
        else
        {
            await interactions.RegisterCommandsGloballyAsync();
            log.LogInformation("Commands registered globally");
        }

        log.LogInformation("Connected as {User} in {GuildCount} guild(s)",
            client.CurrentUser?.Username, client.Guilds.Count);
    }

    /// <summary>Discord.Net's own log stream, mapped onto Serilog levels.</summary>
    private Task ForwardLog(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

        log.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
