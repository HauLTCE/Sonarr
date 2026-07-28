using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Entities.Web;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// In-memory <c>web.*</c> storage. Mirrors the two things the real repository settles in SQL:
/// single-use is a compare-and-set, and revoke-all reports the hashes it touched.
/// </summary>
public sealed class FakeWebAuthRepository : IWebAuthRepository
{
    private readonly Dictionary<string, LoginToken> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WebSession> _sessions = new(StringComparer.Ordinal);

    public List<WebAudit> Audits { get; } = [];

    public Task AddTokenAsync(LoginToken token, CancellationToken ct = default)
    {
        // One outstanding code per person, same as the real repository.
        foreach (var stale in _tokens.Where(t => t.Value.UserId == token.UserId).Select(t => t.Key).ToArray())
        {
            _tokens.Remove(stale);
        }

        token.CreatedAt = token.CreatedAt == default ? DateTimeOffset.UtcNow : token.CreatedAt;
        _tokens[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<LoginToken?> GetTokenAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(_tokens.TryGetValue(tokenHash, out var token) ? token : null);

    public Task<bool> ConsumeTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        if (!_tokens.TryGetValue(tokenHash, out var token) || token.Used)
        {
            return Task.FromResult(false);
        }

        token.Used = true;
        return Task.FromResult(true);
    }

    public Task<int> DeleteTokensAsync(long userId, CancellationToken ct = default)
    {
        string[] hashes = [.. _tokens.Where(t => t.Value.UserId == userId).Select(t => t.Key)];
        foreach (var hash in hashes)
        {
            _tokens.Remove(hash);
        }

        return Task.FromResult(hashes.Length);
    }

    public Task AddSessionAsync(WebSession session, CancellationToken ct = default)
    {
        session.CreatedAt = session.CreatedAt == default ? DateTimeOffset.UtcNow : session.CreatedAt;
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task<WebSession?> GetSessionAsync(string sessionHash, CancellationToken ct = default) =>
        Task.FromResult(_sessions.TryGetValue(sessionHash, out var session) ? session : null);

    public Task RenewSessionAsync(string sessionHash, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionHash, out var session))
        {
            session.ExpiresAt = expiresAt;
        }

        return Task.CompletedTask;
    }

    public Task<bool> RevokeSessionAsync(string sessionHash, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionHash, out var session) || session.Revoked)
        {
            return Task.FromResult(false);
        }

        session.Revoked = true;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> RevokeAllSessionsAsync(long userId, CancellationToken ct = default)
    {
        List<string> revoked = [];
        foreach (var session in _sessions.Values.Where(s => s.UserId == userId && !s.Revoked))
        {
            session.Revoked = true;
            revoked.Add(session.SessionId);
        }

        return Task.FromResult<IReadOnlyList<string>>(revoked);
    }

    public Task<IReadOnlyList<WebSession>> GetActiveSessionsAsync(long userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WebSession>>(
        [
            .. _sessions.Values
                .Where(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > DateTimeOffset.UtcNow)
                .OrderByDescending(s => s.CreatedAt),
        ]);

    public Task AddAuditAsync(WebAudit entry, CancellationToken ct = default)
    {
        Audits.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebAudit>> GetAuditAsync(int skip, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WebAudit>>(
            [.. Audits.OrderByDescending(a => a.At).Skip(skip).Take(Math.Clamp(take, 1, 200))]);

    /// <summary>Test helper: the row behind a hash, so a test can age or revoke it.</summary>
    public WebSession Session(string hash) => _sessions[hash];

    public LoginToken Token(string hash) => _tokens[hash];

    public int TokenCount => _tokens.Count;
}

/// <summary>
/// The Redis mirror, in a dictionary. <see cref="Blind"/> makes it behave like a cache miss on
/// every read, which is how the fail-open path gets exercised.
/// </summary>
public sealed class FakeWebSessionCache : IWebSessionCache
{
    private readonly Dictionary<string, WebSessionSnapshot> _sessions = new(StringComparer.Ordinal);

    public bool Blind { get; set; }

    public string? LiveStatus { get; private set; }

    public int Removals { get; private set; }

    public Task<WebSessionSnapshot?> GetSessionAsync(string sessionHash, CancellationToken cancellationToken = default)
        => Task.FromResult(
            !Blind && _sessions.TryGetValue(sessionHash, out var snapshot) ? snapshot : null);

    public Task SetSessionAsync(
        string sessionHash, WebSessionSnapshot session, CancellationToken cancellationToken = default)
    {
        _sessions[sessionHash] = session;
        return Task.CompletedTask;
    }

    public Task RemoveSessionAsync(string sessionHash, CancellationToken cancellationToken = default)
    {
        Removals++;
        _sessions.Remove(sessionHash);
        return Task.CompletedTask;
    }

    public Task<string?> GetLiveStatusJsonAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LiveStatus);

    public Task SetLiveStatusJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        LiveStatus = json;
        return Task.CompletedTask;
    }

    public bool Mirrors(string hash) => _sessions.ContainsKey(hash);
}
