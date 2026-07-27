using Sonarr.Domain.Chat;
using Sonarr.Domain.Entities.Chat;

namespace Sonarr.Domain.Abstractions;

/// <summary>
/// chat.person and the rows that hang off a turn. Postgres is the authority; Redis only
/// hot-caches the person row.
/// </summary>
public interface IPersonRepository
{
    /// <summary>The person row, or null for someone she has never spoken to.</summary>
    Task<Person?> GetAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Commits one turn: the person row plus its facts, episodes and relationship events, in a
    /// single transaction (docs/10).
    /// </summary>
    Task SaveTurnAsync(ChatTurnWrite write, CancellationToken ct = default);

    /// <summary>Active facts for a person, newest first — <c>/memories</c> and hedging read these.</summary>
    Task<IReadOnlyList<Fact>> GetFactsAsync(long guildId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes one fact by predicate (<c>/memories forget</c>). Superseded rows stay for
    /// trajectory. Returns false when there was nothing active to forget.
    /// </summary>
    Task<bool> ForgetFactAsync(long guildId, long userId, string predicate, CancellationToken ct = default);

    /// <summary>Register movement over a window, oldest first — feeds trend lines and tier moments.</summary>
    Task<IReadOnlyList<RelationshipEvent>> GetRecentEventsAsync(
        long guildId,
        long userId,
        DateTimeOffset since,
        int limit,
        CancellationToken ct = default);
}
