using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Music;

/// <summary>
/// music.user_prefs — per (guild, user) music settings. Durable on purpose:
/// the old bot lost these on restart.
/// </summary>
public class MusicUserPrefs : AuditedEntity
{
    public long GuildId { get; set; }

    public long UserId { get; set; }

    /// <summary>Null = use the guild default.</summary>
    public int? Volume { get; set; }

    /// <summary>jsonb array of saved track objects.</summary>
    public JsonArray Favorites { get; set; } = [];
}
