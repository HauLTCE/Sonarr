namespace Sonarr.Application.Chat;

/// <summary>
/// The whole engine state as flat JSON, for the <c>chat:hot:{guild}:{user}</c> cache
/// (5 min sliding). A hit skips the Postgres read mid-conversation.
/// </summary>
/// <remarks>
/// Deliberately not the <c>Person</c> entity: this also carries the topic stack and pending
/// questions, which have no column, and it holds every register flat rather than five typed
/// fields plus an overflow map.
/// </remarks>
public sealed record ChatStateSnapshot
{
    public long Turn { get; init; }

    public IReadOnlyList<string> Activities { get; init; } = [];

    public IReadOnlyDictionary<string, double> Registers { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public IReadOnlyList<TopicSnapshot> Topics { get; init; } = [];

    public IReadOnlyList<PendingSnapshot> Pending { get; init; } = [];

    /// <summary>Intent id → logical turn it last fired.</summary>
    public IReadOnlyDictionary<string, long> Fired { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Slots { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string? AssignedNickname { get; init; }
}

public sealed record TopicSnapshot(string Topic, double Weight);

public sealed record PendingSnapshot(string Slot, long AskedAtTurn);
