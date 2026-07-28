namespace Sonarr.Domain.Chat;

/// <summary>
/// One entry in <c>chat.guild_state.event_log</c>: something the room did, so she can say "last
/// time this many people were online…" (docs/10).
/// </summary>
/// <remarks>
/// Deliberately just a kind, a number and a time. Nothing here identifies a member, which is what
/// lets the log be read out loud in a public channel (docs/06).
/// </remarks>
/// <param name="Kind">Event family — see <see cref="OnlineRecord"/>.</param>
/// <param name="Value">Whatever the kind counts. The online count, for a record.</param>
public sealed record GuildEvent(string Kind, int Value, DateTimeOffset At)
{
    /// <summary>Most people ever seen online at once.</summary>
    public const string OnlineRecord = "online_record";
}
