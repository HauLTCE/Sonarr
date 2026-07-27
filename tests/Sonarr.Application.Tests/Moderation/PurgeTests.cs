using Sonarr.Application.Moderation;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Tests.Moderation;

/// <summary>
/// The highest blast-radius command in the bot. The load-bearing assertion is
/// <see cref="Preview_deletes_nothing_and_files_no_case"/>: with <c>preview:true</c> the deleter
/// delegate must never be invoked and no case row may appear.
/// </summary>
public sealed class PurgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preview_deletes_nothing_and_files_no_case()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        var deleterCalls = 0;

        PurgePreview result = await service.PurgeAsync(
            Build.Guild,
            channelId: 500UL,
            Build.Actor,
            new PurgeFilter(10, Preview: true),
            Recent(5),
            (ids, ct) =>
            {
                deleterCalls++;
                return Task.FromResult(ids.Count);
            });

        Assert.Equal(0, deleterCalls);
        Assert.True(result.WasDryRun);
        Assert.Equal(5, result.Matched);
        Assert.Equal(0, result.Deleted);
        Assert.Empty(cases.Rows);
    }

    [Fact]
    public async Task A_live_run_deletes_and_files_one_case()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        IReadOnlyList<ulong>? deleted = null;

        PurgePreview result = await service.PurgeAsync(
            Build.Guild,
            channelId: 500UL,
            Build.Actor,
            new PurgeFilter(10),
            Recent(4),
            (ids, ct) =>
            {
                deleted = ids;
                return Task.FromResult(ids.Count);
            });

        Assert.NotNull(deleted);
        Assert.Equal(4, result.Deleted);
        Assert.False(result.WasDryRun);

        CaseRecord record = Assert.Single(cases.Rows);
        Assert.Equal(CaseAction.Purge, record.Action);
    }

    [Fact]
    public async Task A_live_run_that_matches_nothing_deletes_nothing_and_files_nothing()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        var called = false;

        PurgePreview result = await service.PurgeAsync(
            Build.Guild, 500UL, Build.Actor,
            new PurgeFilter(10, FromUserId: 99_999UL),
            Recent(3),
            (ids, ct) =>
            {
                called = true;
                return Task.FromResult(ids.Count);
            });

        Assert.False(called);
        Assert.Equal(0, result.Deleted);
        Assert.Empty(cases.Rows);
    }

    [Fact]
    public async Task Case_context_records_counts_and_filters_never_content()
    {
        (Sonarr.Application.Moderation.ModerationService service, FakeModCaseRepository cases, _) =
            Build.Moderation();

        List<PurgeCandidate> candidates =
        [
            new(1UL, 10UL, false, "the secret plan", Now, false),
            new(2UL, 10UL, false, "the secret plan again", Now, false),
        ];

        await service.PurgeAsync(
            Build.Guild, 500UL, Build.Actor,
            new PurgeFilter(10, Contains: "secret"),
            candidates,
            (ids, ct) => Task.FromResult(ids.Count));

        CaseRecord record = Assert.Single(cases.Rows);

        // The mod's own search needle is theirs; the messages it matched are not recorded.
        Assert.DoesNotContain("secret plan", string.Join("|", record.Context.Values), StringComparison.Ordinal);
        Assert.DoesNotContain("secret plan", record.Reason, StringComparison.Ordinal);
        Assert.Equal("2", record.Context["matched"]);
        Assert.Equal("2", record.Context["deleted"]);
    }

    [Fact]
    public void Planner_returns_the_same_counts_for_preview_and_live()
    {
        List<PurgeCandidate> candidates = Recent(6);

        PurgePreview dry = PurgePlanner.Plan(new PurgeFilter(4, Preview: true), candidates, Now);
        PurgePreview live = PurgePlanner.Plan(new PurgeFilter(4), candidates, Now);

        Assert.Equal(dry.Matched, live.Matched);
        Assert.Equal(dry.MessageIds, live.MessageIds);
    }

    [Fact]
    public void Count_caps_the_matches_newest_first()
    {
        PurgePreview plan = PurgePlanner.Plan(new PurgeFilter(2), Recent(5), Now);

        Assert.Equal(2, plan.Matched);

        // Ids are returned oldest-first, but the two chosen are the newest two of five (4 and 5).
        Assert.Equal([4UL, 5UL], plan.MessageIds);
    }

    [Fact]
    public void Pinned_messages_are_skipped_and_counted()
    {
        List<PurgeCandidate> candidates =
        [
            new(1UL, 10UL, false, "a", Now, IsPinned: true),
            new(2UL, 10UL, false, "b", Now, IsPinned: false),
        ];

        PurgePreview plan = PurgePlanner.Plan(new PurgeFilter(10), candidates, Now);

        Assert.Equal(1, plan.Pinned);
        Assert.Equal([2UL], plan.MessageIds);
    }

    [Fact]
    public void Messages_older_than_fourteen_days_are_skipped_and_counted()
    {
        List<PurgeCandidate> candidates =
        [
            new(1UL, 10UL, false, "old", Now.AddDays(-20), false),
            new(2UL, 10UL, false, "new", Now.AddMinutes(-1), false),
        ];

        PurgePreview plan = PurgePlanner.Plan(new PurgeFilter(10), candidates, Now);

        Assert.Equal(1, plan.TooOld);
        Assert.Equal([2UL], plan.MessageIds);
    }

    [Fact]
    public void Bots_filter_keeps_only_bot_authors()
    {
        List<PurgeCandidate> candidates =
        [
            new(1UL, 10UL, AuthorIsBot: true, "beep", Now, false),
            new(2UL, 11UL, AuthorIsBot: false, "hello", Now, false),
        ];

        PurgePreview plan = PurgePlanner.Plan(new PurgeFilter(10, BotsOnly: true), candidates, Now);

        Assert.Equal([1UL], plan.MessageIds);
    }

    [Fact]
    public void From_filter_keeps_only_that_author()
    {
        PurgePreview plan = PurgePlanner.Plan(
            new PurgeFilter(10, FromUserId: 11UL),
            [
                new(1UL, 10UL, false, "a", Now, false),
                new(2UL, 11UL, false, "b", Now, false),
                new(3UL, 11UL, false, "c", Now, false),
            ],
            Now);

        Assert.Equal([2UL, 3UL], plan.MessageIds);
        Assert.Equal(2, plan.AuthorBreakdown[11UL]);
    }

    [Fact]
    public void Contains_filter_is_case_insensitive()
    {
        PurgePreview plan = PurgePlanner.Plan(
            new PurgeFilter(10, Contains: "HELLO"),
            [
                new(1UL, 10UL, false, "well hello there", Now, false),
                new(2UL, 10UL, false, "goodbye", Now, false),
            ],
            Now);

        Assert.Equal([1UL], plan.MessageIds);
    }

    private static List<PurgeCandidate> Recent(int count)
        => [.. Enumerable.Range(1, count)
            .Select(i => new PurgeCandidate((ulong)i, 10UL, false, $"message {i}", Now.AddMinutes(-i), false))];
}
