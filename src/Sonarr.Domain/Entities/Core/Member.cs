namespace Sonarr.Domain.Entities.Core;

/// <summary>core.member — per (guild, user) identity cache and user preferences.</summary>
public class Member : AuditedEntity
{
    public long GuildId { get; set; }

    public long UserId { get; set; }

    /// <summary>Cached; refreshed on gateway events.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Cached; refreshed on gateway events.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>Batched out of Redis, not written per message.</summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>Running total, feeds stats and /userstats.</summary>
    public long MessageCount { get; set; }

    /// <summary>IANA id, set via /timezone.</summary>
    public string? Timezone { get; set; }

    /// <summary>Month+day are what get used; year optional.</summary>
    public DateOnly? Birthday { get; set; }

    /// <summary>Future i18n.</summary>
    public string? Locale { get; set; }
}
