using Sonarr.Bot.Discord.Levels;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Application.Tests.Levels;

/// <summary>
/// The activity buffer is the only path that creates <c>core.member</c> rows, so it has to carry
/// the identity fields that row needs — every other write to that table is an UPDATE that silently
/// no-ops without one (<c>/birthday</c>, <c>/timezone</c>, panel login).
/// </summary>
/// <remarks>
/// These are about the carrying, not the writing: the SQL upsert itself is one statement against
/// Postgres and there are no database-backed tests here (docs/09-testing.md).
/// </remarks>
public sealed class ActivityBufferTests
{
    private const ulong Guild = 700;
    private const ulong User = 42;

    [Fact]
    public void A_message_carries_the_identity_fields_the_row_needs()
    {
        var joined = new DateTimeOffset(2023, 4, 5, 6, 0, 0, TimeSpan.Zero);
        var buffer = new ActivityBuffer();

        buffer.Record(Guild, User, "handle", "Nickname", joined);

        MemberActivityDelta delta = Assert.Single(buffer.Drain());
        Assert.Equal((long)Guild, delta.GuildId);
        Assert.Equal((long)User, delta.UserId);
        Assert.Equal(1, delta.MessageCount);
        Assert.Equal("handle", delta.Username);
        Assert.Equal("Nickname", delta.DisplayName);
        Assert.Equal(joined, delta.JoinedAt);
    }

    [Fact]
    public void A_gateway_cache_miss_still_counts_the_message()
    {
        var buffer = new ActivityBuffer();

        // What XpOnMessage passes when guild.GetUser returns null. The names stay empty so the
        // upsert's COALESCE leaves any existing ones alone, and JoinedAt null makes the flush fall
        // back to the message time rather than inventing a join date.
        buffer.Record(Guild, User);

        MemberActivityDelta delta = Assert.Single(buffer.Drain());
        Assert.Equal(1, delta.MessageCount);
        Assert.Equal(string.Empty, delta.Username);
        Assert.Null(delta.JoinedAt);
    }

    [Fact]
    public void A_rename_mid_window_lands_and_the_first_join_date_sticks()
    {
        var joined = new DateTimeOffset(2023, 4, 5, 6, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var buffer = new ActivityBuffer();

        buffer.Record(Guild, User, "old", "Old", joined);
        buffer.Record(Guild, User, "new", "New", later);

        MemberActivityDelta delta = Assert.Single(buffer.Drain());
        Assert.Equal(2, delta.MessageCount);
        Assert.Equal("new", delta.Username);
        Assert.Equal("New", delta.DisplayName);

        // first_seen_at is only ever set on insert; re-reading the join date cannot improve it.
        Assert.Equal(joined, delta.JoinedAt);
    }

    [Fact]
    public void A_later_cache_miss_does_not_blank_a_name_already_seen()
    {
        var buffer = new ActivityBuffer();

        buffer.Record(Guild, User, "handle", "Nickname", DateTimeOffset.UnixEpoch);
        buffer.Record(Guild, User);

        MemberActivityDelta delta = Assert.Single(buffer.Drain());
        Assert.Equal(2, delta.MessageCount);
        Assert.Equal("handle", delta.Username);
        Assert.Equal("Nickname", delta.DisplayName);
        Assert.Equal(DateTimeOffset.UnixEpoch, delta.JoinedAt);
    }

    [Fact]
    public void Draining_twice_does_not_double_count()
    {
        var buffer = new ActivityBuffer();

        buffer.Record(Guild, User, "handle", "Nickname", DateTimeOffset.UnixEpoch);
        Assert.Single(buffer.Drain());

        Assert.Empty(buffer.Drain());
    }
}
