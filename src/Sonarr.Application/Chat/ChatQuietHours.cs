using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Configuration;

namespace Sonarr.Application.Chat;

/// <summary>
/// The guild's clock and the two hours it can be told to stay quiet in — the restored
/// <c>SLEEP_MODE_ENABLED</c> and <c>MIDDAY_BREAK_ENABLED</c> (docs/10 chat gates,
/// <see cref="QuietHours"/> for the windows).
/// </summary>
/// <remarks>
/// <para>
/// Both halves live in one class because they share the one config read a turn is allowed. The zone
/// is needed regardless — her calendar and mood-of-the-day are the room's, not the container's — so
/// resolving it once and handing the same local time to the gate and to <c>ClockSignals</c> keeps
/// the turn at exactly one <c>timezone</c> lookup, which
/// <c>ChatCalendarTests.Her_calendar_costs_one_config_read_a_turn</c> pins.
/// </para>
/// <para>
/// The flag lookups are deliberately inside the window checks. Outside 22:00–06:00 and the 12:00
/// hour — which is most of the day — a turn asks the feature gate nothing at all, so restoring these
/// toggles costs the warm path a clock comparison and no I/O.
/// </para>
/// </remarks>
public sealed class ChatQuietHours(IFeatureGate features, IGuildConfigService config)
{
    /// <summary>
    /// The zone whose calendar decides her overlays and mood of the day, UTC when the guild has
    /// not set one.
    /// </summary>
    /// <remarks>
    /// Deliberately the guild's zone and never the speaker's: an overlay replaces a pool for the
    /// whole room, so if it followed whoever happened to be typing she would be in October for
    /// one person and not for the next. <c>InvariantGlobalization</c> means IANA ids only resolve
    /// where tzdata exists (see ZoneResolver) — hence the UTC fallback rather than a throw.
    /// </remarks>
    public async Task<TimeZoneInfo> ZoneAsync(ulong guildId, CancellationToken ct = default)
    {
        ConfigValue? configured = await config.GetAsync(guildId, ConfigKeys.Timezone, ct)
            .ConfigureAwait(false);

        return configured?.Raw is { } id
               && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.Utc;
    }

    /// <summary>
    /// The <see cref="ChatDecision.SkipReasons"/> value for a turn that lands in an enabled quiet
    /// window, or null when she may answer.
    /// </summary>
    /// <param name="local">Now, already converted into the guild's zone by <see cref="ZoneAsync"/>.</param>
    public async Task<string?> SkipReasonAsync(
        ulong guildId, DateTimeOffset local, CancellationToken ct = default)
    {
        if (QuietHours.IsAsleep(local)
            && await features.IsEnabledAsync(FeatureNames.Sleep, guildId, ct).ConfigureAwait(false))
        {
            return ChatDecision.SkipReasons.Asleep;
        }

        if (QuietHours.IsMiddayBreak(local)
            && await features.IsEnabledAsync(FeatureNames.Midday, guildId, ct).ConfigureAwait(false))
        {
            return ChatDecision.SkipReasons.MiddayBreak;
        }

        return null;
    }
}
