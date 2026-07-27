using System.Text.Json.Nodes;

namespace Sonarr.Domain.Entities.Chat;

/// <summary>
/// chat.person — engine state per (guild, user). Hot-cached in Redis
/// (<c>chat:hot:{guild}:{user}</c>, write-through), but this row is the authority.
/// </summary>
public class Person : AuditedEntity
{
    public long GuildId { get; set; }

    public long UserId { get; set; }

    /// <summary>Current FSM / activity state.</summary>
    public string DialogueState { get; set; } = string.Empty;

    /// <summary>jsonb, typed: anger, boredom, fondness, trust, grudge.</summary>
    public PersonRegisters Registers { get; set; } = new();

    /// <summary>jsonb: short-term slots, each carrying its own TTL envelope.</summary>
    public JsonObject Slots { get; set; } = new();

    /// <summary>Determinism contract: monotonic per-person turn counter.</summary>
    public long LogicalClock { get; set; }

    /// <summary>Authored tiers: stranger → … → inner_circle.</summary>
    public string RelationshipTier { get; set; } = string.Empty;

    /// <summary>The name she picked for you; null until she picks one.</summary>
    public string? AssignedNickname { get; set; }

    /// <summary>jsonb: once:/cooldown: firing log. Persisted — fixes the old amnesia bug.</summary>
    public JsonObject FiredLog { get; set; } = new();

    /// <summary>jsonb array: persisted push/pop activity stack.</summary>
    public JsonArray ActivityStack { get; set; } = [];
}
