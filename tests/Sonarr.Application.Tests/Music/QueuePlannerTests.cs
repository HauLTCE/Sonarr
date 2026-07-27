using Sonarr.Application.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Application.Tests.Music;

/// <summary>Paging, fair-queue ordering, duplicate detection and tracks-until-yours.</summary>
public sealed class QueuePlannerTests
{
    [Fact]
    public void A_page_past_the_end_clamps_to_the_last_page()
    {
        IReadOnlyList<TrackInfo> queue = [.. Enumerable.Range(1, 25).Select(i => Build.Track($"t{i}", Build.Member))];

        QueuePage page = QueuePlanner.Page(null, queue, 99);

        Assert.Equal(3, page.Page);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(5, page.Entries.Count);
        Assert.Equal(21, page.Entries[0].Position);
    }

    [Fact]
    public void An_empty_queue_is_one_empty_page()
    {
        QueuePage page = QueuePlanner.Page(Build.Track("now", Build.Member), [], 1);

        Assert.Equal(1, page.TotalPages);
        Assert.Empty(page.Entries);
        Assert.Equal(0, page.TotalTracks);
        Assert.NotNull(page.Current);
    }

    [Fact]
    public void Fair_order_alternates_between_requesters()
    {
        IReadOnlyList<TrackInfo> queue =
        [
            Build.Track("a1", 1),
            Build.Track("a2", 1),
            Build.Track("a3", 1),
            Build.Track("b1", 2),
            Build.Track("b2", 2),
        ];

        IReadOnlyList<TrackInfo> fair = QueuePlanner.FairOrder(queue);

        Assert.Equal(["a1", "b1", "a2", "b2", "a3"], fair.Select(t => t.Title));
    }

    [Fact]
    public void Fair_order_leaves_a_single_requester_alone()
    {
        IReadOnlyList<TrackInfo> queue = [Build.Track("a1", 1), Build.Track("a2", 1)];

        Assert.Same(queue, QueuePlanner.FairOrder(queue));
    }

    [Fact]
    public void Duplicate_positions_keep_the_earliest_copy_and_come_back_descending()
    {
        IReadOnlyList<TrackInfo> queue =
        [
            Build.Track("a", 1, "https://x/1"),
            Build.Track("b", 1, "https://x/2"),
            Build.Track("c", 1, "https://x/1"),
            Build.Track("d", 1, "https://x/2"),
        ];

        Assert.Equal([4, 3], QueuePlanner.DuplicatePositions(queue));
    }

    [Fact]
    public void Tracks_until_yours_counts_what_plays_first()
    {
        IReadOnlyList<TrackInfo> queue = [Build.Track("a", 1), Build.Track("b", 2), Build.Track("c", 7)];

        Assert.Equal(2, QueuePlanner.TracksUntil(queue, 7));
        Assert.Null(QueuePlanner.TracksUntil(queue, 99));
    }
}

/// <summary>Smart autoplay never seeds from something the room voted down.</summary>
public sealed class AutoplaySeederTests
{
    [Fact]
    public void The_seed_skips_disliked_and_excluded_uris()
    {
        IReadOnlyList<TrackPlayCount> history =
        [
            new("hated", "https://x/1", 9),
            new("current", "https://x/2", 5),
            new("fine", "https://x/3", 2),
        ];

        var seed = AutoplaySeeder.PickSeed(history, ["https://x/1"], ["https://x/2"]);

        Assert.Equal("https://x/3", seed);
    }

    [Fact]
    public void Nothing_left_to_seed_from_returns_null()
    {
        IReadOnlyList<TrackPlayCount> history = [new("hated", "https://x/1", 9)];

        Assert.Null(AutoplaySeeder.PickSeed(history, ["https://x/1"]));
        Assert.Null(AutoplaySeeder.PickSeed([], []));
    }
}

/// <summary>The two thresholds that decide whether a skip needs a vote.</summary>
public sealed class MusicRulesTests
{
    [Theory]
    [InlineData(8, 5)]
    [InlineData(9, 5)]
    [InlineData(2, 2)]
    [InlineData(1, 2)]
    public void Votes_needed_is_a_majority_with_a_floor_of_two(int listeners, int expected)
        => Assert.Equal(expected, MusicRules.VotesNeeded(listeners));
}
