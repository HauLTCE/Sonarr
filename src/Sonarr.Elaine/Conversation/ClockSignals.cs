using System.Globalization;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Conversation;

/// <summary>
/// Wall-clock time, reduced to the handful of signals a turn is allowed to see.
/// </summary>
/// <remarks>
/// This is the whole of docs/10's "the adapter feeds wall-clock signals": absence tiers,
/// multi-day decay, the 3–6 am pool, the mood-of-the-day seed, seasonal overlays. Deriving
/// them is pure — <see cref="From"/> takes the instants rather than reading a clock — so a
/// stored turn replays to the same reply a year later, which an ambient <c>UtcNow</c> would
/// make impossible.
/// </remarks>
public sealed record ClockSignals
{
    /// <summary>
    /// Decay steps per hour away. Anger (0.5/turn) burns off within the first hour either way;
    /// this exists so a grudge (0.02/turn) measurably cools over days rather than never.
    /// </summary>
    public const int StepsPerHourAway = 1;

    /// <summary>
    /// Ceiling on catch-up decay, roughly a month. Without it, a person returning after a year
    /// would have every register snapped to baseline by one multiplication, which is just
    /// "forget everyone who takes a holiday".
    /// </summary>
    public const int MaxExtraDecaySteps = 720;

    /// <summary>Overlay ids active this turn, highest priority first.</summary>
    public IReadOnlyList<string> Overlays { get; init; } = [];

    /// <summary>Catch-up decay for the time since the last turn.</summary>
    public int ExtraDecaySteps { get; init; }

    /// <summary>How long they were gone, as an authored bucket, or null when they never left.</summary>
    public string? AbsenceTier { get; init; }

    /// <summary>
    /// Mood-of-the-day seed. Mixed into <see cref="ConversationState.Salt"/> so the same
    /// person asking the same thing gets a different draw tomorrow.
    /// </summary>
    public ulong DaySeed { get; init; }

    /// <summary>
    /// Absence buckets. Authored lines key off these ids, not off an hour count; null is
    /// "still here", which is most turns.
    /// </summary>
    public static class Absence
    {
        public const string Hours = "hours";
        public const string Days = "days";
        public const string Weeks = "weeks";
    }

    /// <summary>
    /// Derives the signals for a turn happening at <paramref name="now"/>.
    /// </summary>
    /// <param name="lastSeen">
    /// The person's previous turn, or null for first contact — which is not an absence, so it
    /// gets no catch-up decay and no "long time no see" tier.
    /// </param>
    public static ClockSignals From(PersonaGraph persona, DateTimeOffset now, DateTimeOffset? lastSeen)
    {
        ArgumentNullException.ThrowIfNull(persona);

        DateTimeOffset local = now.ToLocalTime();
        Dictionary<string, int> fields = new(StringComparer.Ordinal)
        {
            [OverlayActivation.Fields.Hour] = local.Hour,
            [OverlayActivation.Fields.Month] = local.Month,
            [OverlayActivation.Fields.Day] = local.Day,
            [OverlayActivation.Fields.DayOfWeek] = (int)local.DayOfWeek,
        };

        TimeSpan away = lastSeen is { } seen && now > seen ? now - seen : TimeSpan.Zero;

        return new ClockSignals
        {
            Overlays = OverlayActivation.Active(persona, fields),
            ExtraDecaySteps = (int)Math.Min(
                away.TotalHours * StepsPerHourAway,
                MaxExtraDecaySteps),
            AbsenceTier = TierFor(away),
            DaySeed = StableHash.Of(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };
    }

    private static string? TierFor(TimeSpan away) => away.TotalHours switch
    {
        < 6 => null,
        < 24 => Absence.Hours,
        < 24 * 7 => Absence.Days,
        _ => Absence.Weeks,
    };
}
