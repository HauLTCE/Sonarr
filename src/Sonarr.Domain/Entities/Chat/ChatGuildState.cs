using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>chat.guild_state — engine state that is about the room, not one user.</summary>
public class ChatGuildState : AuditedEntity
{
    public long GuildId { get; set; }

    /// <summary>jsonb: aggregate room mood registers.</summary>
    public JsonObject RoomMood { get; set; } = new();

    /// <summary>Guild-wide determinism counter.</summary>
    public long GlobalClock { get; set; }

    /// <summary>jsonb array: server events ("movie night", online-count records).</summary>
    public JsonArray EventLog { get; set; } = [];
}
