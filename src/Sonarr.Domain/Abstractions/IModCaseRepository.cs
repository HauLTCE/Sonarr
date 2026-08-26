using Sonarr.Domain.Moderation;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// mod.case + mod.infraction_summary access. Returns domain records, never EF entities,
/// so the persistence shape stops at this boundary.
/// </summary>
public interface IModCaseRepository
{
    /// <summary>Inserts the case and returns it with the number Postgres assigned.</summary>
    Task<CaseRecord> AddAsync(NewCase newCase, CancellationToken ct = default);

    /// <summary><c>null</c> when the id does not exist in this guild — cases never cross guilds.</summary>
    Task<CaseRecord?> GetAsync(long guildId, long caseId, CancellationToken ct = default);

    /// <summary>
    /// One page of /modlog, newest first. <paramref name="targetId"/> <c>null</c> = the whole guild.
    /// <paramref name="page"/> is 1-based; out-of-range pages come back empty, not as an error.
    /// </summary>
    Task<CasePage> GetPageAsync(
        long guildId,
        long? targetId,
        int page,
        CancellationToken ct = default);

    /// <summary>mod.infraction_summary row for one member, or <see cref="InfractionTally.Empty"/>.</summary>
    Task<InfractionTally> GetTallyAsync(long guildId, long targetId, CancellationToken ct = default);
}
