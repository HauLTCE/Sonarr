using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;

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
        client.JoinedGuild += OnJoinedGuildAsync;

        await interactions.AddModulesAsync(typeof(DiscordGatewayService).Assembly, services);

        await client.LoginAsync(TokenType.Bot, options.DiscordToken);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        client.JoinedGuild -= OnJoinedGuildAsync;
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

        foreach (SocketGuild guild in client.Guilds)
        {
            await SyncGuildAsync(guild);
        }
    }

    private Task OnJoinedGuildAsync(SocketGuild guild) => SyncGuildAsync(guild);

    /// <summary>
    /// Makes sure <c>core.guild</c> has a row for this guild, and refreshes the cached name.
    /// <para>
    /// Not optional bookkeeping: every area table cascades from <c>core.guild</c>, so the first XP
    /// award or activity flush in a guild with no row fails on the foreign key. Ready covers the
    /// guilds we were already in (including a rename while offline); JoinedGuild covers new ones.
    /// </para>
    /// </summary>
    private async Task SyncGuildAsync(SocketGuild guild)
    {
        try
        {
            using IServiceScope scope = services.CreateScope();
            var guilds = scope.ServiceProvider.GetRequiredService<IGuildRepository>();

            // CreatedAt is when *we* joined, which is what core.guild.joined_at means. Discord only
            // exposes it on the bot's own member object, so fall back to now() when it is not cached.
            DateTimeOffset joinedAt = guild.CurrentUser?.JoinedAt ?? DateTimeOffset.UtcNow;

            await guilds.UpsertAsync((long)guild.Id, guild.Name, joinedAt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Logged rather than thrown: one unreachable guild must not stop the gateway coming up.
            // The cost is that guild's writes failing on the FK until the next Ready, which is loud.
            log.LogError(ex, "Could not sync core.guild for {GuildId}", guild.Id);
        }
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
