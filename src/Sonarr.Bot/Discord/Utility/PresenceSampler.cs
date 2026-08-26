using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// Samples every guild's online and in-voice counts every 5 minutes into <c>stats.activity_sample</c>
/// (docs/08-background-services.md), so the activity series exists without asking Discord for
/// history it does not keep.
/// </summary>
public sealed class PresenceSampler(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<PresenceSampler> log) : BackgroundService
{
    /// <summary>docs/08: 5 min sample.</summary>
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("PresenceSampler sampling every {Minutes} min", SampleInterval.TotalMinutes);

        using PeriodicTimer timer = new(SampleInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SampleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Presence sample failed");
            }
        }
    }

    /// <summary>One sample pass. Internal so a test can drive it without the timer.</summary>
    internal async Task SampleAsync(CancellationToken ct)
    {
        if (client.ConnectionState != global::Discord.ConnectionState.Connected)
        {
            // Mid-reconnect the caches are empty; a zero row would be a lie.
            return;
        }

        // Truncated to the hour: the schema keys on (guild_id, hour_bucket), so the twelve samples
        // in an hour overwrite one row rather than growing the table.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset bucket = new(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);

        using IServiceScope scope = scopes.CreateScope();
        var stats = scope.ServiceProvider.GetRequiredService<IStatsRepository>();
        var presence = scope.ServiceProvider.GetRequiredService<IPresenceCache>();
        var guildState = scope.ServiceProvider.GetRequiredService<IGuildStateRepository>();

        foreach (SocketGuild guild in client.Guilds)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // One pass over the user cache per guild — no REST calls, no LINQ chain per member.
            // This is the whole cost of the sampler on a J2900.
            var online = 0;
            foreach (SocketGuildUser member in guild.Users)
            {
                if (!member.IsBot && member.Status != global::Discord.UserStatus.Offline)
                {
                    online++;
                }
            }

            var voice = 0;
            foreach (SocketVoiceChannel channel in guild.VoiceChannels)
            {
                voice += channel.ConnectedUsers.Count(u => !u.IsBot);
            }

            try
            {
                await stats.RecordActivityAsync((long)guild.Id, bucket, online, voice, 0, ct)
                    .ConfigureAwait(false);

                // Levels' voice accrual reads this to decide whether a channel counts as busy.
                await presence.SetOnlineSampleAsync(guild.Id, online, ct).ConfigureAwait(false);

                // Server-event memory (docs/10): only a new high writes, so this is one read on a
                // quiet server and the log stays a few rows for the guild's whole life.
                if (await guildState
                        .RecordOnlineRecordAsync((long)guild.Id, online, now, ct)
                        .ConfigureAwait(false))
                {
                    log.LogInformation(
                        "New online record for guild {GuildId}: {Online}", guild.Id, online);
                }
            }
            catch (Exception ex)
            {
                // One guild failing must not skip the rest of the sample.
                log.LogWarning(ex, "Presence sample failed for guild {GuildId}", guild.Id);
            }
        }
    }
}
