using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sonarr.Domain.Chat;

/// <summary>
/// Reading and appending <c>chat.guild_state.event_log</c> (docs/04). Pure: the repository loads
/// the row and saves it, the rules live here.
/// </summary>
/// <remarks>
/// The log is a jsonb array on the guild's one row, not a table — it holds a handful of entries
/// per guild forever and is read whole or not at all, so a table would be an index and a join for
/// nothing.
/// </remarks>
public static class GuildEventLog
{
    /// <summary>
    /// Entries kept per guild. Room for the record history plus whatever kinds get added later;
    /// past that the oldest go, because "the record before the record before this one" is not
    /// something she brings up.
    /// </summary>
    public const int MaxEvents = 50;

    /// <summary>
    /// Entries this build understands, newest first. Anything malformed or written by another
    /// shape is skipped rather than thrown over: a bad log must not cost her a reply.
    /// </summary>
    public static IReadOnlyList<GuildEvent> Read(JsonArray? log)
    {
        if (log is null)
        {
            return [];
        }

        List<GuildEvent> events = [];
        foreach (JsonNode? node in log)
        {
            if (node is null)
            {
                continue;
            }

            try
            {
                if (node.Deserialize<GuildEvent>(Json) is { Kind.Length: > 0 } entry)
                {
                    events.Add(entry);
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return [.. events.OrderByDescending(e => e.At)];
    }

    /// <summary>The standing value for <paramref name="kind"/>, or 0 when there is none.</summary>
    public static int Best(JsonArray? log, string kind)
        => Read(log)
            .Where(e => string.Equals(e.Kind, kind, StringComparison.Ordinal))
            .Select(e => e.Value)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// The log with an online-count record appended, or null when <paramref name="online"/> does
    /// not beat the standing one and nothing should be written.
    /// </summary>
    /// <remarks>
    /// Returning null rather than an unchanged array is what lets the caller skip the save
    /// entirely, which is the common case on every one of the 5-minute passes.
    /// </remarks>
    public static JsonArray? WithOnlineRecord(JsonArray? log, int online, DateTimeOffset at)
    {
        // A zero or a negative is a mid-reconnect cache, not a quiet server; a record of nobody
        // would be a lie.
        if (online <= 0 || online <= Best(log, GuildEvent.OnlineRecord))
        {
            return null;
        }

        List<GuildEvent> events = [new GuildEvent(GuildEvent.OnlineRecord, online, at), .. Read(log)];
        if (events.Count > MaxEvents)
        {
            events.RemoveRange(MaxEvents, events.Count - MaxEvents);
        }

        return new JsonArray([.. events.Select(e => JsonSerializer.SerializeToNode(e, Json))]);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
