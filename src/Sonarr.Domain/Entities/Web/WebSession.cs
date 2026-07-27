namespace Sonarr.Domain.Entities.Web;

/// <summary>
/// web.session — panel session. Id is a 256-bit random value, hashed at rest;
/// Redis mirrors it for fast validation but this row is the authority for revocation.
/// </summary>
public class WebSession : AuditedEntity
{
    /// <summary>SHA-256 of the raw session id; PK.</summary>
    public string SessionId { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>24 h, or 90 d when remember_me was checked.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set by "log out everywhere" and admin revocation.</summary>
    public bool Revoked { get; set; }

    public string? UserAgent { get; set; }
}
