using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Utility;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Utility;

/// <inheritdoc cref="IMilestoneService"/>
/// <remarks>
/// <para>Nothing is scheduled here. Both milestones are a <em>query</em> against a date the guild's
/// timezone decides — the day's list is derived, not enqueued — so there is no state to keep in
/// sync, nothing to backfill when someone sets a birthday for tomorrow, and a missed day costs a
/// greeting rather than leaving a stale job row pointing at a member who left.</para>
/// <para>Behind an interface because <see cref="ZoneResolver"/> is internal to this assembly, same
/// as capsules and events.</para>
/// </remarks>
internal sealed class MilestoneService(
    IMemberRepository members,
    ZoneResolver zones,
    PersonaHolder persona,
    ILogger<MilestoneService> log) : IMilestoneService
{
    public async Task<BirthdayResult> SetBirthdayAsync(
        ulong guildId,
        ulong userId,
        int month,
        int day,
        int? year,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = await GuildTodayAsync(guildId, cancellationToken);

        if (!MilestoneRules.TryBuildBirthday(month, day, year, today, out DateOnly birthday, out var error))
        {
            return BirthdayResult.Rejected(error);
        }

        await members.SetBirthdayAsync((long)guildId, (long)userId, birthday, cancellationToken);
        log.LogInformation("Birthday set for {UserId} in guild {GuildId}", userId, guildId);

        var when = $"{birthday.Day} {MonthName(birthday.Month)}";
        var age = MilestoneRules.HasYear(birthday)
            ? $" You'll be {MilestoneRules.YearsSince(birthday, today) + 1}. I'll pretend to be surprised."
            : " No year, no maths. Fine by me.";

        return BirthdayResult.Ok($"Noted: {when}.{age}");
    }

    public async Task<BirthdayResult> ClearBirthdayAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        await members.SetBirthdayAsync((long)guildId, (long)userId, null, cancellationToken);
        return BirthdayResult.Ok("Forgotten. You get no cake from me.");
    }

    public async Task<AnniversaryCard> GetAnniversaryAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        Member? member = await members.GetAsync((long)guildId, (long)userId, cancellationToken);
        DateOnly today = await GuildTodayAsync(guildId, cancellationToken);

        if (member is null || member.FirstSeenAt == default)
        {
            return new AnniversaryCard(userId, null, 0, null, 0);
        }

        DateOnly joined = DateOnly.FromDateTime(member.FirstSeenAt.UtcDateTime);
        DateOnly next = MilestoneRules.NextOccurrence(joined.Month, joined.Day, today);

        return new AnniversaryCard(
            userId,
            member.FirstSeenAt,
            MilestoneRules.YearsSince(joined, today),
            next,
            next.DayNumber - today.DayNumber);
    }

    public async Task<IReadOnlyList<Milestone>> TodayAsync(
        ulong guildId,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = await GuildTodayAsync(guildId, cancellationToken);
        List<Milestone> found = [];

        // 29 February is stored as itself and observed on 1 March in a common year, so that one day
        // asks twice. Every other day asks once.
        foreach ((int month, int day) in Days(today))
        {
            foreach (Member m in await members.GetBirthdaysAsync((long)guildId, month, day, cancellationToken))
            {
                found.Add(new Milestone(
                    MilestoneKind.Birthday,
                    (ulong)m.UserId,
                    m.Birthday is { } b ? MilestoneRules.AgeOn(b, today) ?? 0 : 0));
            }

            foreach (Member m in await members.GetJoinAnniversariesAsync(
                (long)guildId, month, day, cancellationToken))
            {
                var years = MilestoneRules.YearsSince(
                    DateOnly.FromDateTime(m.FirstSeenAt.UtcDateTime), today);

                // Year zero is the join itself — "happy 0th" on someone's first day is a bug, not
                // a welcome, and WelcomeFlow already said hello.
                if (years > 0)
                {
                    found.Add(new Milestone(MilestoneKind.Anniversary, (ulong)m.UserId, years));
                }
            }
        }

        return found;
    }

    public string Line(Milestone milestone)
    {
        ArgumentNullException.ThrowIfNull(milestone);

        var pool = milestone.Kind == MilestoneKind.Birthday
            ? MilestoneRules.BirthdayPool
            : MilestoneRules.AnniversaryPool;

        // Seeded on the person and the year being marked, so the line is stable across a retry
        // within the same day but different next year.
        return new LinePicker(persona.Current).Pick(
            pool,
            modeId: null,
            new TurnSeededRandom((long)milestone.UserId + milestone.Years, StableHash.Of(pool)),
            NoSlots) ?? string.Empty;
    }

    public async Task<DateTimeOffset> LocalNowAsync(
        ulong guildId, CancellationToken cancellationToken = default)
    {
        TimeZoneInfo zone = await zones.ForGuildAsync(guildId, cancellationToken);
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
    }

    private static IEnumerable<(int Month, int Day)> Days(DateOnly today)
    {
        yield return (today.Month, today.Day);
        if (MilestoneRules.ObservesLeapDay(today))
        {
            yield return (2, 29);
        }
    }

    private async Task<DateOnly> GuildTodayAsync(ulong guildId, CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = await zones.ForGuildAsync(guildId, cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date);
    }

    private static string MonthName(int month) => MonthNames[month - 1];

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    private static readonly IReadOnlyDictionary<string, string> NoSlots =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
