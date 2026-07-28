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

    public async Task<Member> UpsertAsync(
        long guildId,
        long userId,
        string username,
        string displayName,
        CancellationToken ct = default)
    {
        Member? member = await db.Members
            .FirstOrDefaultAsync(m => m.GuildId == guildId && m.UserId == userId, ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (member is null)
        {
            member = new Member
            {
                GuildId = guildId,
                UserId = userId,
                Username = Trim(username),
                DisplayName = Trim(displayName),
                FirstSeenAt = now,
                LastActiveAt = now,
            };
            db.Members.Add(member);
        }
        else
        {
            member.Username = Trim(username);
            member.DisplayName = Trim(displayName);
            member.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return member;
    }

    public async Task ApplyActivityAsync(
        IReadOnlyCollection<MemberActivityDelta> deltas,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
        {
            return;
        }

        // ponytail: one UPDATE per member. Fine at this scale (a flush touches a few
        // rows); if a flush ever spans hundreds, swap for a single UNNEST-join update.
        foreach (MemberActivityDelta d in deltas)
        {
            await db.Members
                .Where(m => m.GuildId == d.GuildId && m.UserId == d.UserId)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(m => m.MessageCount, m => m.MessageCount + d.MessageCount)
                        .SetProperty(m => m.LastActiveAt, d.LastActiveAt)
                        .SetProperty(m => m.UpdatedAt, d.LastActiveAt),
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
