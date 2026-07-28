using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMemberRepository"/>
public sealed class MemberRepository(SonarrDbContext db) : IMemberRepository
{
    public Task<Member?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => db.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.GuildId == guildId && m.UserId == userId, ct);

    public async Task ApplyActivityAsync(
        IReadOnlyCollection<MemberActivityDelta> deltas,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
        {
            return;
        }

        // An upsert, not an update: this is the only path that inserts into core.member, so a
        // member who has never spoken before gets their row here. Everything else that writes to
        // this table (timezone, birthday) is an ExecuteUpdate that silently no-ops without one.
        //
        // first_seen_at is set on insert only and never touched again — it is what /anniversary
        // reports, so a later flush must not move it. COALESCE keeps Discord's own join date when
        // the gateway had the member cached and falls back to now() when it did not.
        //
        // ponytail: one statement per member. Fine at this scale (a 60 s flush touches a few
        // rows); if a flush ever spans hundreds, swap for a single UNNEST-joined upsert.
        foreach (MemberActivityDelta d in deltas)
        {
            string username = Trim(d.Username);
            string displayName = Trim(d.DisplayName);

            // Resolved here rather than with a SQL COALESCE: a null DateTimeOffset? loses its CLR
            // type passing through FormattableString, and Npgsql cannot infer the type of a bare
            // null parameter. One non-null value goes to the server instead.
            DateTimeOffset firstSeenAt = d.JoinedAt ?? d.LastActiveAt;

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO core.member
                    (guild_id, user_id, username, display_name,
                     first_seen_at, last_active_at, message_count, created_at, updated_at)
                VALUES
                    ({d.GuildId}, {d.UserId}, {username}, {displayName},
                     {firstSeenAt}, {d.LastActiveAt}, {d.MessageCount},
                     now(), now())
                ON CONFLICT (guild_id, user_id) DO UPDATE
                SET message_count = core.member.message_count + {d.MessageCount},
                    last_active_at = {d.LastActiveAt},
                    username = COALESCE(NULLIF({username}, ''), core.member.username),
                    display_name = COALESCE(NULLIF({displayName}, ''), core.member.display_name),
                    updated_at = {d.LastActiveAt}
                """,
                ct);
        }
    }

    public Task SetTimezoneAsync(
        long guildId,
        long userId,
        string? ianaTimezone,
        CancellationToken ct = default)
        => db.Members
            .Where(m => m.GuildId == guildId && m.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Timezone, ianaTimezone)
                    .SetProperty(m => m.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    public Task SetBirthdayAsync(
        long guildId,
        long userId,
        DateOnly? birthday,
        CancellationToken ct = default)
        => db.Members
            .Where(m => m.GuildId == guildId && m.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Birthday, birthday)
                    .SetProperty(m => m.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

    public async Task<IReadOnlyList<Member>> GetBirthdaysAsync(
        long guildId,
        int month,
        int day,
        CancellationToken ct = default)
        => await db.Members
            .AsNoTracking()
            .Where(m => m.GuildId == guildId
                && m.Birthday != null
                && m.Birthday.Value.Month == month
                && m.Birthday.Value.Day == day)
            .ToListAsync(ct);

    // ponytail: month+day matched in UTC, not in the guild's zone. A member first seen within a few
    // hours of midnight UTC can therefore be greeted a day either side in a far-west/far-east
    // guild. Fixing it properly needs `first_seen_at AT TIME ZONE <guild zone>` in the WHERE, which
    // is a raw-SQL query and an index that no longer helps; upgrade there if anyone ever notices.
    public async Task<IReadOnlyList<Member>> GetJoinAnniversariesAsync(
        long guildId,
        int month,
        int day,
        CancellationToken ct = default)
        => await db.Members
            .AsNoTracking()
            .Where(m => m.GuildId == guildId
                && m.FirstSeenAt.Month == month
                && m.FirstSeenAt.Day == day)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MemberGuild>> GetGuildsAsync(
        long userId, CancellationToken ct = default)
        => await (from m in db.Members
                  join g in db.Guilds on m.GuildId equals g.GuildId
                  where m.UserId == userId
                  orderby g.Name
                  select new MemberGuild(g.GuildId, g.Name))
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<long?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // Distinct user ids, then Take(2): the same person has a row per guild, so one id across
        // many rows is the normal case. Two distinct ids means the handle is ambiguous and the
        // caller must not guess — see the interface remark.
        List<long> ids = await db.Members
            .AsNoTracking()
            .Where(m => m.Username.ToLower() == username.ToLower())
            .Select(m => m.UserId)
            .Distinct()
            .Take(2)
            .ToListAsync(ct);

        return ids.Count == 1 ? ids[0] : null;
    }

    private static string Trim(string value)
        => value.Length <= 64 ? value : value[..64];
}
