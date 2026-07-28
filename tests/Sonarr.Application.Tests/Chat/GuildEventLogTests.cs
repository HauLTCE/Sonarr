using System.Text.Json;
using System.Text.Json.Nodes;
using Sonarr.Domain.Chat;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// Server-event memory: "last time this many people were online…" (docs/10). Only a beaten record
/// writes, and the log is counts and timestamps — never who was in the room.
/// </summary>
public sealed class GuildEventLogTests
{
    [Fact]
    public void The_first_sample_is_the_record()
    {
        JsonArray log = Assert.IsType<JsonArray>(
            GuildEventLog.WithOnlineRecord(null, 12, Build.Now));

        GuildEvent entry = Assert.Single(GuildEventLog.Read(log));
        Assert.Equal(GuildEvent.OnlineRecord, entry.Kind);
        Assert.Equal(12, entry.Value);
        Assert.Equal(Build.Now, entry.At);
    }

    [Fact]
    public void A_quieter_sample_writes_nothing()
    {
        // The whole reason this returns null: the sampler runs every 5 minutes forever and must
        // not touch the row on a normal pass.
        Assert.Null(GuildEventLog.WithOnlineRecord(Log((GuildEvent.OnlineRecord, 12)), 11, Build.Now));
    }

    [Fact]
    public void Matching_the_record_is_not_beating_it()
    {
        Assert.Null(GuildEventLog.WithOnlineRecord(Log((GuildEvent.OnlineRecord, 12)), 12, Build.Now));
    }

    [Fact]
    public void Beating_it_keeps_the_old_record_behind_the_new_one()
    {
        JsonArray log = Assert.IsType<JsonArray>(
            GuildEventLog.WithOnlineRecord(Log((GuildEvent.OnlineRecord, 12)), 20, Build.Now));

        Assert.Equal([20, 12], GuildEventLog.Read(log).Select(e => e.Value));
    }

    [Fact]
    public void An_empty_cache_is_not_a_quiet_server()
    {
        // Mid-reconnect every guild reads zero online. A record of nobody would be a lie, and it
        // would also make every later sample a "new record".
        Assert.Null(GuildEventLog.WithOnlineRecord(null, 0, Build.Now));
        Assert.Null(GuildEventLog.WithOnlineRecord(null, -1, Build.Now));
    }

    [Fact]
    public void The_log_does_not_grow_without_bound()
    {
        JsonArray? log = null;
        for (var online = 1; online <= GuildEventLog.MaxEvents + 10; online++)
        {
            log = GuildEventLog.WithOnlineRecord(log, online, Build.Now.AddMinutes(online));
        }

        IReadOnlyList<GuildEvent> events = GuildEventLog.Read(log);
        Assert.Equal(GuildEventLog.MaxEvents, events.Count);
        Assert.Equal(GuildEventLog.MaxEvents + 10, events[0].Value);
    }

    [Fact]
    public void A_log_written_by_something_else_is_skipped_not_thrown_over()
    {
        JsonArray log =
        [
            JsonValue.Create(42),
            new JsonObject { ["kind"] = "", ["value"] = 3, ["at"] = Build.Now },
            JsonSerializer.SerializeToNode(new GuildEvent(GuildEvent.OnlineRecord, 9, Build.Now)),
        ];

        GuildEvent entry = Assert.Single(GuildEventLog.Read(log));
        Assert.Equal(9, entry.Value);
    }

    [Fact]
    public void Reading_nothing_is_an_empty_log_not_a_null()
    {
        Assert.Empty(GuildEventLog.Read(null));
        Assert.Empty(GuildEventLog.Read([]));
        Assert.Equal(0, GuildEventLog.Best(null, GuildEvent.OnlineRecord));
    }

    [Fact]
    public void Another_kind_of_event_does_not_set_the_online_record()
    {
        JsonArray log = Log(("movie_night", 99));

        Assert.Equal(0, GuildEventLog.Best(log, GuildEvent.OnlineRecord));
        Assert.NotNull(GuildEventLog.WithOnlineRecord(log, 5, Build.Now));
    }

    [Fact]
    public void Nothing_in_an_entry_identifies_a_member()
    {
        // docs/06: this log is read out loud in a public channel, so it may only ever hold a kind,
        // a count and a time. A property that could carry a user id would break that.
        Assert.Equal(
            ["Kind", "Value", "At"],
            typeof(GuildEvent).GetProperties().Select(p => p.Name).Except(["EqualityContract"]));
    }

    private static JsonArray Log(params (string Kind, int Value)[] entries)
        => new([.. entries.Select(e => JsonSerializer.SerializeToNode(
            new GuildEvent(e.Kind, e.Value, Build.Now),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))]);
}
