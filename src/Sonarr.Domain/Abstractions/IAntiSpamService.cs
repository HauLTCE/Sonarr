using Sonarr.Domain.Moderation;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// AntiSpam v2 (docs/08-background-services.md): identical-flood, mass-mention, invite-link.
/// Called once per guild message, so the clean path must stay allocation-light.
/// </summary>
public interface IAntiSpamService
{
    /// <summary>
    /// Decides whether a message is spam under the guild's policy. Returns
    /// <see cref="SpamVerdict.Clean"/> for anything that is not, including every message from a
    /// bot, a moderator, or a guild with anti-spam turned off.
    /// </summary>
    /// <remarks>
    /// The identical-flood rule needs <c>rl:spam:{guild}:{user}</c>. That store fails OPEN
    /// (docs/05-caching.md, <c>ICooldownStore.RecordMessageHashAsync</c>): a Redis outage must
    /// never auto-punish a user, so flood detection degrades to "first time seen". The
    /// content-only rules (mentions, invites) keep working regardless.
    /// </remarks>
    Task<SpamVerdict> EvaluateAsync(SpamCandidate message, CancellationToken ct = default);
}
