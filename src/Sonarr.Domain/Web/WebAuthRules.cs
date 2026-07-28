using System.Security.Cryptography;
using System.Text;

namespace Sonarr.Domain.Web;

/// <summary>
/// The whole of the DM-token login arithmetic: alphabet, generation, hashing, lifetimes and the
/// sliding-renewal decision (docs/09-web-panels.md).
/// </summary>
/// <remarks>
/// Pure and public on purpose. Every security property the doc lists — unambiguous alphabet,
/// CSPRNG, hashed at rest, single-use, 10 minutes, 24 h / 90 d sessions — is a decision that can
/// be asserted here without a database, a web server or a Discord connection.
/// </remarks>
public static class WebAuthRules
{
    /// <summary>
    /// 32 characters, no <c>0 O 1 I</c>: the code is read off a phone screen and typed into a
    /// browser, so the pairs people mistype are simply absent rather than "helpfully" mapped.
    /// </summary>
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public const int TokenLength = 8;

    /// <summary>Wrong codes allowed against one person's outstanding token before it dies.</summary>
    public const int MaxVerifyAttempts = 5;

    public const string LoginPurpose = "login";

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);

    public static readonly TimeSpan RememberLifetime = TimeSpan.FromDays(90);

    /// <summary>8 CSPRNG characters from <see cref="Alphabet"/>. Never logged, never stored raw.</summary>
    public static string NewToken() => RandomNumberGenerator.GetString(Alphabet, TokenLength);

    /// <summary>256 bits as hex — the raw session id, only ever in the cookie. Hex needs no
    /// cookie-value escaping, which base64 would.</summary>
    public static string NewSessionId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 hex of a raw secret. 64 chars, matching the column width.</summary>
    public static string Hash(string raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(raw);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    /// <summary>Display form for the DM: <c>ABCD-2345</c>. Easier to read back than eight run-on chars.</summary>
    public static string Format(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return token.Length == TokenLength ? $"{token[..4]}-{token[4..]}" : token;
    }

    /// <summary>
    /// What the visitor typed, back to canonical form — case and the display dash are forgiven,
    /// anything outside the alphabet is not. Returns <c>null</c> when the input cannot be a token,
    /// so the caller rejects it without touching the database.
    /// </summary>
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Length > 32)
        {
            return null;
        }

        StringBuilder sb = new(TokenLength);
        foreach (var c in typed)
        {
            if (c is ' ' or '-' or '_')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (!Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                return null;
            }

            if (sb.Length == TokenLength)
            {
                return null;
            }

            sb.Append(upper);
        }

        return sb.Length == TokenLength ? sb.ToString() : null;
    }

    /// <summary>A Discord handle, trimmed of the decoration people paste with it.</summary>
    public static string? NormalizeUsername(string? typed)
    {
        var name = typed?.Trim().TrimStart('@');
        return string.IsNullOrEmpty(name) || name.Length > 64 ? null : name;
    }

    public static DateTimeOffset TokenExpiry(DateTimeOffset now) => now + TokenLifetime;

    public static DateTimeOffset SessionExpiry(bool remember, DateTimeOffset now)
        => now + (remember ? RememberLifetime : SessionLifetime);

    /// <summary>
    /// Was this session issued with "remember me"? Derived from its own window rather than stored:
    /// the two lifetimes are 24 h and 90 d, so anything past a week can only be the long one.
    /// </summary>
    public static bool IsRemembered(DateTimeOffset createdAt, DateTimeOffset expiresAt)
        => expiresAt - createdAt > TimeSpan.FromDays(7);

    /// <summary>
    /// Sliding renewal: extend only once the session is past half its life. Renewing on every
    /// request would write a row per page view for no extra safety.
    /// </summary>
    /// <remarks>
    /// An already-expired session gets nothing. The caller checks expiry first, but a renewal
    /// helper that hands a fresh 24 h window to a dead session is one missed guard away from
    /// being a session-resurrection bug.
    /// </remarks>
    public static DateTimeOffset? Renewal(DateTimeOffset createdAt, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt <= now)
        {
            return null;
        }

        var remember = IsRemembered(createdAt, expiresAt);
        TimeSpan lifetime = remember ? RememberLifetime : SessionLifetime;

        if (expiresAt - now > lifetime / 2)
        {
            return null;
        }

        return SessionExpiry(remember, now);
    }

    public static bool IsUsable(bool used, DateTimeOffset expiresAt, DateTimeOffset now)
        => !used && expiresAt > now;
}
