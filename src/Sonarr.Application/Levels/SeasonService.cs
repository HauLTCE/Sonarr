using Microsoft.Extensions.Logging;
using Sonarr.Application.Utility;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Levels;

/// <inheritdoc cref="ISeasonService"/>
/// <remarks>
/// <para><b>Seasons are calendar months, not configured windows.</b> docs/08 says "month boundary",
/// so the window is derived from the guild's own clock rather than stored as policy: nothing to
/// configure, nothing to get wrong, and a guild that has never heard of seasons still gets a
/// coherent history.</para>
/// <para><b>The first season is opened lazily.</b> A guild with no open season gets the current
/// month opened on the next roll, so there is no bootstrap step at join time and no backfill for
/// the guilds already in the database. The consequence is that a guild's first season starts when
/// the bot first rolls it rather than at the instant it joined, which is the honest answer anyway —
/// nobody counted the XP before that.</para>
/// <para><b>Nothing is reset.</b> <c>levels.progress.xp</c> is a lifetime total; a season's XP is
/// the slice of it no closed season has claimed (<see cref="SeasonStanding"/>). So closing a season
/// takes away nobody's level, and a close that fails leaves the next one able to catch up.</para>
/// </remarks>
internal sealed class SeasonService(
    ISeasonRepository seasons,
    ZoneResolver zones,
    PersonaHolder persona,
    ILogger<SeasonService> log) : ISeasonService
{
    public async Task<SeasonRoll?> RollAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        TimeZoneInfo zone = await zones.ForGuildAsync(guildId, cancellationToken);
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        (DateTimeOffset starts, DateTimeOffset ends) = SeasonRules.MonthWindow(localNow);

        Season? active = await seasons.GetActiveAsync((long)guildId, cancellationToken);

        if (active is null)
        {
            // Nothing open: this guild is new to seasons, or a previous roll closed one and died
            // before opening the next. Either way the current month is the right window.
            await seasons.OpenAsync((long)guildId, starts, ends, cancellationToken);
            log.LogInformation(
                "Opened season for guild {GuildId}: {Label}", guildId, SeasonRules.Label(starts));
            return null;
        }

        if (!SeasonRules.IsOver(active.EndsAt, localNow))
        {
            return null;
        }

        SeasonRoll? roll = await CloseAsync(guildId, active, cancellationToken);

        // Opened after the close in the same pass so the guild is never without a season for longer
        // than one poll — and if this half fails, the branch above picks it up next tick.
        await seasons.OpenAsync((long)guildId, starts, ends, cancellationToken);
        log.LogInformation(
            "Rolled guild {GuildId} into {Label}", guildId, SeasonRules.Label(starts));

        return roll;
    }

    public string Line(SeasonRoll roll)
    {
        ArgumentNullException.ThrowIfNull(roll);

        // Seeded on the season, so a retried announcement reads the same and next month differs.
        return new LinePicker(persona.Current).Pick(
            SeasonRules.ClosePool,
            modeId: null,
            new TurnSeededRandom(roll.SeasonId, StableHash.Of(SeasonRules.ClosePool)),
            NoSlots) ?? string.Empty;
    }

    private async Task<SeasonRoll?> CloseAsync(
        ulong guildId, Season active, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeasonStanding> standings =
            await seasons.GetStandingsAsync((long)guildId, cancellationToken);

        IReadOnlyList<SeasonResult> results = SeasonRules.Rank(standings);

        if (!await seasons.CloseAsync(active.SeasonId, results, cancellationToken))
        {
            // Someone else closed it between the read and the write. Their results stand.
            log.LogInformation(
                "Season {SeasonId} was already closed for guild {GuildId}", active.SeasonId, guildId);
            return null;
        }

        log.LogInformation(
            "Closed season {SeasonId} for guild {GuildId} with {Count} result(s)",
            active.SeasonId, guildId, results.Count);

        return new SeasonRoll(
            active.SeasonId,
            SeasonRules.Label(active.StartsAt),
            results.Count,
            [
                .. results
                    .Take(SeasonRules.TopCount)
                    // Level is not a season fact (docs/04), same as the frozen leaderboard page.
                    .Select(r => new LeaderboardEntry(r.Rank, (ulong)r.UserId, r.XpEarned, 0)),
            ]);
    }

    private static readonly IReadOnlyDictionary<string, string> NoSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
