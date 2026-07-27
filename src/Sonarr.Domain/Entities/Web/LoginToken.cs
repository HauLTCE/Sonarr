using System.Net;

namespace Sonarr.Domain.Entities.Web;

/// <summary>
/// web.login_token — single-use DM login code, stored SHA-256 hashed.
/// The raw token only ever exists in the DM (docs/04-database.md, docs/09-web-panels.md).
/// </summary>
public class LoginToken : AuditedEntity
{
    /// <summary>SHA-256 of the raw token; PK. Never store the raw value.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>Currently only <c>login</c>.</summary>
    public string Purpose { get; set; } = "login";

    /// <summary>10 minutes after issue.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Single-use: flipped on successful verify.</summary>
    public bool Used { get; set; }

    /// <summary>inet column; the IP that asked for the code (abuse tracing).</summary>
    public IPAddress? RequestedIp { get; set; }
}
