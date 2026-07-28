using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Web;

namespace Sonarr.Infrastructure.Persistence.Repositories.Web;

/// <inheritdoc cref="IWebAuthRepository"/>
public sealed class WebAuthRepository(SonarrDbContext db) : IWebAuthRepository
{
    public async Task AddTokenAsync(LoginToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        // One outstanding code per person: asking again invalidates the last one, so a DM the
        // visitor never received cannot be replayed later by whoever did receive it.
        await db.LoginTokens
            .Where(t => t.UserId == token.UserId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        db.LoginTokens.Add(token);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<LoginToken?> GetTokenAsync(string tokenHash, CancellationToken ct = default)
        => await db.LoginTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

    public async Task<bool> ConsumeTokenAsync(string tokenHash, CancellationToken ct = default)
        // Single-use in the WHERE clause: two verifies of the same code race, one updates a row.
        => await db.LoginTokens
            .Where(t => t.TokenHash == tokenHash && !t.Used)
            .ExecuteUpdateAsync(
                t => t
                    .SetProperty(x => x.Used, true)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false) > 0;

    public async Task<int> DeleteTokensAsync(long userId, CancellationToken ct = default)
        => await db.LoginTokens
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

    public async Task AddSessionAsync(WebSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        db.WebSessions.Add(session);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<WebSession?> GetSessionAsync(string sessionHash, CancellationToken ct = default)
        => await db.WebSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionHash, ct)
            .ConfigureAwait(false);

    public async Task RenewSessionAsync(
        string sessionHash, DateTimeOffset expiresAt, CancellationToken ct = default)
        => await db.WebSessions
            .Where(s => s.SessionId == sessionHash && !s.Revoked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.ExpiresAt, expiresAt)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);

    public async Task<bool> RevokeSessionAsync(string sessionHash, CancellationToken ct = default)
        => await db.WebSessions
            .Where(s => s.SessionId == sessionHash && !s.Revoked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Revoked, true)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false) > 0;

    public async Task<IReadOnlyList<string>> RevokeAllSessionsAsync(
        long userId, CancellationToken ct = default)
    {
        // Read the hashes first: the caller has to drop the Redis mirrors, and after the update
        // there is no way to tell which rows this call revoked from ones already revoked.
        List<string> hashes = await db.WebSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.Revoked)
            .Select(s => s.SessionId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (hashes.Count == 0)
        {
            return hashes;
        }

        await db.WebSessions
            .Where(s => s.UserId == userId && !s.Revoked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(x => x.Revoked, true)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct)
            .ConfigureAwait(false);

        return hashes;
    }

    public async Task AddAuditAsync(WebAudit entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        db.WebAudits.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebAudit>> GetAuditAsync(
        int skip, int take, CancellationToken ct = default)
        => await db.WebAudits
            .AsNoTracking()
            .OrderByDescending(a => a.At)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
