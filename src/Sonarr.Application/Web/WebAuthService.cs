using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Entities.Web;
using Sonarr.Domain.Web;

namespace Sonarr.Application.Web;

/// <inheritdoc cref="IWebAuthService"/>
/// <remarks>
/// <para>Two rules shape this class. The request endpoint is <em>uniform</em>: unknown handle,
/// throttled, closed DMs and a successful issue all leave by the same door, so the response body
/// carries no information about who exists. And nothing raw is ever persisted: the token and the
/// session id are hashed on the way in, so this service can hand out a credential it could not
/// itself reconstruct afterwards.</para>
/// <para>The Redis mirror is a read cache with the row as the authority. Sensitive routes ask for
/// a fresh read (<c>requireFresh</c>), which is what makes "log out everywhere" immediate.</para>
/// </remarks>
internal sealed class WebAuthService(
    IWebAuthRepository repo,
    IMemberRepository members,
    ICooldownStore cooldowns,
    IWebSessionCache cache,
    AdminAllowList admins,
    ILogger<WebAuthService> log) : IWebAuthService
{
    public async Task<LoginRequest> RequestTokenAsync(
        string? username, string? sourceIp, CancellationToken cancellationToken = default)
    {
        var handle = WebAuthRules.NormalizeUsername(username);
        if (handle is null)
        {
            return LoginRequest.BadInput;
        }

        // Both limits, and the IP first: an attacker enumerating handles burns their own IP window
        // before they can spend anyone else's. Checked before resolution so a miss costs a quota
        // slot too — otherwise unknown handles would be free to probe.
        if (!await cooldowns.TryConsumeLoginAsync($"ip:{sourceIp ?? "unknown"}", cancellationToken)
            || !await cooldowns.TryConsumeLoginAsync($"user:{handle.ToLowerInvariant()}", cancellationToken))
        {
            log.LogWarning("Login token request throttled for {Handle} from {Ip}", handle, sourceIp);
            return LoginRequest.Throttled;
        }

        long? userId = await members.FindUserIdByUsernameAsync(handle, cancellationToken);
        if (userId is null)
        {
            log.LogInformation("Login token requested for unknown handle from {Ip}", sourceIp);
            return LoginRequest.Unknown;
        }

        var raw = WebAuthRules.NewToken();
        await repo.AddTokenAsync(
            new LoginToken
            {
                TokenHash = WebAuthRules.Hash(raw),
                UserId = userId.Value,
                Purpose = WebAuthRules.LoginPurpose,
                ExpiresAt = WebAuthRules.TokenExpiry(DateTimeOffset.UtcNow),
                RequestedIp = ParseIp(sourceIp),
            },
            cancellationToken);

        log.LogInformation("Login token issued for user {UserId}", userId.Value);
        return new LoginRequest(LoginRequestOutcome.Issued, (ulong)userId.Value, raw);
    }

    public async Task<LoginResult> VerifyAsync(
        string? username,
        string? code,
        bool remember,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var handle = WebAuthRules.NormalizeUsername(username);
        var token = WebAuthRules.Normalize(code);
        if (handle is null || token is null)
        {
            return LoginResult.BadCode;
        }

        long? userId = await members.FindUserIdByUsernameAsync(handle, cancellationToken);
        if (userId is null)
        {
            return LoginResult.BadCode;
        }

        // The attempt cap is per user, not per submitted code: counting submissions would let an
        // attacker walk the keyspace by never repeating a guess.
        if (!await cooldowns.TryConsumeVerifyAsync($"user:{userId.Value}", cancellationToken))
        {
            await repo.DeleteTokensAsync(userId.Value, cancellationToken);
            log.LogWarning("Login attempts exhausted for user {UserId}; token discarded", userId.Value);
            return LoginResult.TooManyAttempts;
        }

        LoginToken? row = await repo.GetTokenAsync(WebAuthRules.Hash(token), cancellationToken);

        // The row is found by hash, so "belongs to someone else" is a real case: a code is only
        // valid for the handle it was issued to.
        if (row is null || row.UserId != userId.Value)
        {
            return LoginResult.BadCode;
        }

        if (!WebAuthRules.IsUsable(row.Used, row.ExpiresAt, DateTimeOffset.UtcNow))
        {
            return LoginResult.Expired;
        }

        // Single-use is settled by the database, not by the check above: two verifies of the same
        // code race here and exactly one gets the row.
        if (!await repo.ConsumeTokenAsync(row.TokenHash, cancellationToken))
        {
            return LoginResult.Expired;
        }

        var rawSession = WebAuthRules.NewSessionId();
        var hash = WebAuthRules.Hash(rawSession);
        DateTimeOffset expires = WebAuthRules.SessionExpiry(remember, DateTimeOffset.UtcNow);

        await repo.AddSessionAsync(
            new WebSession
            {
                SessionId = hash,
                UserId = userId.Value,
                ExpiresAt = expires,
                UserAgent = Truncate(userAgent, 512),
            },
            cancellationToken);

        await cache.SetSessionAsync(
            hash, new WebSessionSnapshot(userId.Value, expires, remember), cancellationToken);

        log.LogInformation("Panel session issued for user {UserId} (remember: {Remember})", userId.Value, remember);
        return new LoginResult(LoginOutcome.Success, rawSession, (ulong)userId.Value, expires);
    }

    public async Task<PanelUser?> AuthenticateAsync(
        string? rawSessionId, bool requireFresh, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionId) || rawSessionId.Length is < 32 or > 128)
        {
            return null;
        }

        var hash = WebAuthRules.Hash(rawSessionId);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Fast path only for ordinary reads. The mirror cannot outlive the row's expiry (its TTL is
        // that expiry) but it can outlive a revocation, which is exactly what requireFresh is for.
        if (!requireFresh)
        {
            WebSessionSnapshot? cached = await cache.GetSessionAsync(hash, cancellationToken);
            if (cached is not null)
            {
                return cached.ExpiresAt > now
                    ? new PanelUser((ulong)cached.UserId, hash, admins.Contains((ulong)cached.UserId), cached.ExpiresAt)
                    : null;
            }
        }

        WebSession? row = await repo.GetSessionAsync(hash, cancellationToken);
        if (row is null || row.Revoked || row.ExpiresAt <= now)
        {
            if (row is not null)
            {
                await cache.RemoveSessionAsync(hash, cancellationToken);
            }

            return null;
        }

        DateTimeOffset? renewed = WebAuthRules.Renewal(row.CreatedAt, row.ExpiresAt, now);
        if (renewed is { } until)
        {
            await repo.RenewSessionAsync(hash, until, cancellationToken);
        }

        DateTimeOffset expires = renewed ?? row.ExpiresAt;
        await cache.SetSessionAsync(
            hash,
            new WebSessionSnapshot(row.UserId, expires, WebAuthRules.IsRemembered(row.CreatedAt, expires)),
            cancellationToken);

        return new PanelUser(
            (ulong)row.UserId, hash, admins.Contains((ulong)row.UserId), expires, renewed);
    }

    public async Task<bool> LogoutAsync(string? rawSessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionId))
        {
            return false;
        }

        var hash = WebAuthRules.Hash(rawSessionId);

        // Mirror first: if the row update fails the session is at worst still valid in Postgres,
        // whereas dropping the row and keeping the mirror would leave a revoked session working.
        await cache.RemoveSessionAsync(hash, cancellationToken);
        return await repo.RevokeSessionAsync(hash, cancellationToken);
    }

    public async Task<int> LogoutAllAsync(ulong userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> hashes = await repo.RevokeAllSessionsAsync((long)userId, cancellationToken);
        foreach (var hash in hashes)
        {
            await cache.RemoveSessionAsync(hash, cancellationToken);
        }

        log.LogInformation("Revoked {Count} panel sessions for user {UserId}", hashes.Count, userId);
        return hashes.Count;
    }

    public Task AuditAsync(
        PanelUser actor,
        string action,
        string? target,
        ulong guildId = 0,
        object? detail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return repo.AddAuditAsync(
            new WebAudit
            {
                UserId = (long)actor.UserId,
                SessionId = actor.SessionHash,
                GuildId = (long)guildId,
                Action = Truncate(action, 64)!,
                Target = Truncate(target, 256),
                Detail = ToJson(detail),
                At = DateTimeOffset.UtcNow,
            },
            cancellationToken);
    }

    private static JsonObject ToJson(object? detail)
        => detail is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(detail) as JsonObject ?? new JsonObject();

    private static System.Net.IPAddress? ParseIp(string? address)
        => System.Net.IPAddress.TryParse(address, out System.Net.IPAddress? parsed) ? parsed : null;

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
