using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Application.Utility;

/// <summary>
/// "Whose time is it" in one place: the member's own zone, else the guild's, else UTC. Shared by
/// reminders, announcements and <c>/timestamp</c> so they can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// <b>ICU is not available.</b> <c>InvariantGlobalization=true</c> strips ICU, so IANA ids resolve
/// from the OS tz database only: they work in the Linux container (tzdata is installed, see
/// src/Sonarr.Bot/Dockerfile) and fail on a Windows dev box. Every lookup here therefore falls
/// back to UTC instead of throwing — a dev box shows UTC times, prod shows real ones, and neither
/// crashes a reminder.
/// </para>
/// </remarks>
internal sealed class ZoneResolver(IMemberRepository members, IGuildConfigService config)
{
    public async Task<TimeZoneInfo> ForUserAsync(
        ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        Member? member = await members.GetAsync((long)guildId, (long)userId, cancellationToken);
        if (Find(member?.Timezone) is { } own)
        {
            return own;
        }

        return await ForGuildAsync(guildId, cancellationToken);
    }

    public async Task<TimeZoneInfo> ForGuildAsync(ulong guildId, CancellationToken cancellationToken)
    {
        ConfigValue? configured = await config.GetAsync(guildId, ConfigKeys.Timezone, cancellationToken);
        return Find(configured?.Raw) ?? TimeZoneInfo.Utc;
    }

    /// <summary>
    /// <c>null</c> when the id is unset or the runtime can't resolve it. Never throws: an
    /// unresolvable id is a platform gap, not a user error, and the caller's fallback is correct.
    /// </summary>
    public static TimeZoneInfo? Find(string? ianaId)
        => string.IsNullOrWhiteSpace(ianaId)
            ? null
            : TimeZoneInfo.TryFindSystemTimeZoneById(ianaId.Trim(), out TimeZoneInfo? zone) ? zone : null;
}
