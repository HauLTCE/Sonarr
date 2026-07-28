using System.Globalization;
using System.Text;
using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// The month boundary (docs/08 <c>SeasonRoller</c>): closes the guild's season, freezes its
/// standings, announces the top chatters and opens the next one.
/// </summary>
/// <remarks>
/// <para>Polled hourly rather than slept to a computed instant, for the same reason as
/// <c>DailyTick</c>: "the end of the month" is a different moment in every guild's timezone and DST
/// moves it. An hour of lateness on a monthly event is invisible, and the poll makes the service
/// restart-safe without any stored schedule — <see cref="ISeasonService.RollAsync"/> is idempotent,
/// so a tick that finds nothing to do costs one query per guild.</para>
/// <para>The announcement is best-effort and comes <em>after</em> the close has committed. Freezing
/// a month's standings is the durable part; a missing announcement is a shame, but a season left
/// open because the announce channel was deleted would keep counting into next month.</para>
/// </remarks>
public sealed class SeasonRoller(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<SeasonRoller> log) : BackgroundService
{
    /// <summary>
    /// Bounds how late a roll can be. A month has 700-odd hours, so hourly is generous.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("SeasonRoller polling every {Interval}", PollInterval);

        using PeriodicTimer timer = new(PollInterval);

        // Once up front: a bot that has been down over a month boundary should roll on start rather
        // than wait an hour, and a guild with no season at all gets one immediately.
        await TickAsync(stoppingToken).ConfigureAwait(false);

        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One pass over every guild. Public so a test can drive it without waiting for the 1st, same as
    /// <c>DailyTick.TickAsync</c>.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (client.ConnectionState != global::Discord.ConnectionState.Connected)
        {
            return;
        }

        foreach (SocketGuild guild in client.Guilds)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // One guild's problem is its own — a failed close must not cost every guild after it in
            // the list its own roll.
            try
            {
                await RollAsync(guild, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "SeasonRoller failed for guild {GuildId}", guild.Id);
            }
        }
    }

    private async Task RollAsync(SocketGuild guild, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        ISeasonService seasons = scope.ServiceProvider.GetRequiredService<ISeasonService>();

        if (await seasons.RollAsync(guild.Id, ct).ConfigureAwait(false) is not { } roll)
        {
            return;
        }

        IGuildConfigService config = scope.ServiceProvider.GetRequiredService<IGuildConfigService>();
        ConfigValue? configured = await config.GetAsync(guild.Id, ConfigKeys.AnnounceChannel, ct)
            .ConfigureAwait(false);

        if (configured?.AsSnowflake is not { } channelId
            || guild.GetTextChannel(channelId) is not { } channel)
        {
            log.LogDebug(
                "Guild {GuildId} closed season {SeasonId} with no usable announce channel",
                guild.Id, roll.SeasonId);
            return;
        }

        await channel.SendMessageAsync(
            text: seasons.Line(roll),
            embed: Embed(roll),
            // The podium mentions the top three, and being top of the month is worth a ping.
            // Nothing member-authored is in the text, so there is nothing to sanitize.
            allowedMentions: AllowedMentions.All,
            options: new RequestOptions { CancelToken = ct }).ConfigureAwait(false);
    }

    /// <summary>
    /// The podium. Public and static so a test can read it without a gateway.
    /// </summary>
    public static Embed Embed(SeasonRoll roll)
    {
        ArgumentNullException.ThrowIfNull(roll);

        var lines = new StringBuilder();
        foreach (LeaderboardEntry entry in roll.Top)
        {
            var medal = entry.Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"`#{entry.Rank}`" };
            lines.AppendLine(
                $"{medal} <@{entry.UserId}> — {entry.Xp.ToString("N0", CultureInfo.InvariantCulture)} XP");
        }

        if (roll.Top.Count == 0)
        {
            lines.AppendLine("Nobody earned anything. A quiet month.");
        }

        return new EmbedBuilder()
            .WithTitle($"Top chatters — {roll.Label}")
            .WithColor(new Color(0xFEE75C))
            .WithDescription(lines.ToString())
            .WithFooter(roll.Participants == 1
                ? "1 member scored this season."
                : $"{roll.Participants} members scored this season.")
            .Build();
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
