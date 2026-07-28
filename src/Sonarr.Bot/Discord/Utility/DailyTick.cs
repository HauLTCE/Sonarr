using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// The once-a-day greeting pass (docs/08, checklist "DailyTick"): birthdays and join anniversaries,
/// posted in each guild's own morning.
/// </summary>
/// <remarks>
/// <para><b>Why the other listed duties are not here.</b> Three of the four things the checklist
/// hangs off DailyTick turn out to need no scheduled write at all:</para>
/// <list type="bullet">
/// <item>the first-message bonus resets by comparison
/// (<c>StreakRules.FirstMessageBonusDue</c> asks whether the stored day is today), so a nightly
/// write would only re-state what the read already knows;</item>
/// <item>a broken streak is likewise derived on read
/// (<c>StreakRules.CurrentDays</c>), which is strictly better than a sweep — the number is right
/// the moment midnight passes rather than whenever the sweep last ran, and a failed sweep cannot
/// leave a stale streak on someone's card;</item>
/// <item>mood reseed and the seasonal overlay are explicitly excluded (checklist line 253) — both
/// are derived per turn from <c>ClockSignals</c>.</item>
/// </list>
/// <para>What is left is the half that genuinely has to happen <em>at a time</em>: nobody wants a
/// birthday greeting at 04:00, so this polls and each guild is greeted when its own clock reaches
/// <see cref="GreetHour"/>. The poll interval is what bounds the error, so it is well under an hour.
/// </para>
/// <para>Greetings go to <see cref="ConfigKeys.AnnounceChannel"/>. Reusing it rather than adding a
/// key keeps the config catalog closed, and makes the feature opt-in per guild by construction: no
/// announce channel, no greetings, which is the right default for a guild that never asked.</para>
/// </remarks>
public sealed class DailyTick(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<DailyTick> log) : BackgroundService
{
    /// <summary>Guild-local hour the greetings go out. Morning, not midnight.</summary>
    public const int GreetHour = 9;

    /// <summary>
    /// Bounds how late a guild can be greeted, so it has to divide into an hour comfortably.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    /// <summary>Mentions per message. Past this the greeting is split, so nothing is truncated.</summary>
    public const int MentionsPerMessage = 10;

    // ponytail: in-memory, so a restart inside a guild's greeting hour can greet twice. A day-keyed
    // Redis flag (SET NX, 48 h TTL) is the upgrade if it ever actually happens; a duplicate
    // greeting is cosmetic, and the alternative — a durable core.job row per guild — needs
    // bootstrap and cleanup for every join and leave.
    private readonly Dictionary<ulong, DateOnly> _greeted = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "DailyTick greeting at {Hour}:00 guild-local, polling every {Interval}",
            GreetHour, PollInterval);

        using PeriodicTimer timer = new(PollInterval);

        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false))
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One pass over every guild. Public so a test can drive it without waiting for 09:00, same as
    /// <c>RetentionPruner.SweepAsync</c>.
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

            // One guild's problem is its own: a missing channel or a revoked permission must not
            // cost every guild after it in the list its greeting.
            try
            {
                await GreetAsync(guild, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "DailyTick failed for guild {GuildId}", guild.Id);
            }
        }
    }

    private async Task GreetAsync(SocketGuild guild, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        IMilestoneService milestones = scope.ServiceProvider.GetRequiredService<IMilestoneService>();

        DateTimeOffset local = await milestones.LocalNowAsync(guild.Id, ct).ConfigureAwait(false);
        DateOnly today = DateOnly.FromDateTime(local.Date);

        if (local.Hour < GreetHour || Greeted(guild.Id) == today)
        {
            return;
        }

        // Stamped before posting, not after: a Discord failure that repeats every 15 minutes for
        // the rest of the day is worse than a missed greeting, and the retry is tomorrow anyway.
        _greeted[guild.Id] = today;

        IReadOnlyList<Milestone> due = await milestones.TodayAsync(guild.Id, ct).ConfigureAwait(false);
        if (due.Count == 0)
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
                "Guild {GuildId} has {Count} milestone(s) today but no usable announce channel",
                guild.Id, due.Count);
            return;
        }

        foreach ((MilestoneKind kind, IReadOnlyList<Milestone> group) in Group(due))
        {
            foreach (Milestone[] batch in group.Chunk(MentionsPerMessage))
            {
                await channel.SendMessageAsync(
                    text: Compose(kind, batch, milestones.Line(batch[0])),
                    // AllowedMentions.All on purpose: the point of a greeting is that the person
                    // being greeted sees it. Nothing member-authored goes into the text — only
                    // mentions and her own authored line — so there is nothing to sanitize.
                    allowedMentions: AllowedMentions.All,
                    options: new RequestOptions { CancelToken = ct }).ConfigureAwait(false);
            }
        }

        log.LogInformation(
            "DailyTick greeted {Count} member(s) in guild {GuildId}", due.Count, guild.Id);
    }

    /// <summary>
    /// The message for one batch: everyone mentioned once, then a single authored line. One line
    /// per person would be a wall of near-identical snark on a day three people share.
    /// </summary>
    public static string Compose(MilestoneKind kind, IReadOnlyList<Milestone> batch, string line)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var mentions = string.Join(" ", batch.Select(m => MentionUtils.MentionUser(m.UserId)));
        var years = kind == MilestoneKind.Anniversary && batch.All(m => m.Years == batch[0].Years)
            ? $" ({batch[0].Years}y)"
            : string.Empty;

        return $"{mentions}{years} — {line}";
    }

    /// <summary>Birthdays first, then anniversaries. Deterministic order so a test can assert it.</summary>
    public static IEnumerable<(MilestoneKind Kind, IReadOnlyList<Milestone> Group)> Group(
        IReadOnlyList<Milestone> due)
    {
        foreach (MilestoneKind kind in (MilestoneKind[])[MilestoneKind.Birthday, MilestoneKind.Anniversary])
        {
            Milestone[] group = [.. due.Where(m => m.Kind == kind)];
            if (group.Length > 0)
            {
                yield return (kind, group);
            }
        }
    }

    private DateOnly? Greeted(ulong guildId)
        => _greeted.TryGetValue(guildId, out DateOnly day) ? day : null;

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
