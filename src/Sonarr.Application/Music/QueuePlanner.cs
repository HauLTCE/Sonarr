using Sonarr.Domain.Music;

namespace Sonarr.Application.Music;

/// <summary>
/// The queue arithmetic: paging, fair-queue ordering, duplicate detection and
/// "tracks until yours". Pure functions over domain models — no player, no Discord, so the
/// awkward cases (page past the end, one requester, everything a duplicate) are unit-testable.
/// </summary>
public static class QueuePlanner
{
    /// <summary>
    /// One page of <c>/queue</c>. <paramref name="page"/> is 1-based and clamped, so a stale
    /// button or a hand-typed 99 shows the last page instead of an error.
    /// </summary>
    public static QueuePage Page(TrackInfo? current, IReadOnlyList<TrackInfo> queue, int page)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var totalPages = Math.Max(1, (queue.Count + QueuePage.PageSize - 1) / QueuePage.PageSize);
        var clamped = Math.Clamp(page, 1, totalPages);
        var skip = (clamped - 1) * QueuePage.PageSize;

        var entries = new List<QueueEntry>(Math.Min(QueuePage.PageSize, Math.Max(0, queue.Count - skip)));
        for (var i = skip; i < queue.Count && i < skip + QueuePage.PageSize; i++)
        {
            entries.Add(new QueueEntry(i + 1, queue[i]));
        }

        var remaining = TimeSpan.FromMilliseconds(queue.Sum(t => t.DurationMs));
        return new QueuePage(clamped, totalPages, queue.Count, current, entries, remaining);
    }

    /// <summary>
    /// Round-robin across requesters, keeping each requester's own order (<c>/fairqueue</c>).
    /// One person's queue comes back unchanged; a person who queued 20 tracks no longer blocks
    /// everyone behind them.
    /// </summary>
    public static IReadOnlyList<TrackInfo> FairOrder(IReadOnlyList<TrackInfo> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        if (queue.Count < 2)
        {
            return queue;
        }

        // Requester order is first-appearance order, so the person at the front of the queue
        // stays at the front.
        List<List<TrackInfo>> lanes = [];
        Dictionary<ulong, List<TrackInfo>> byRequester = [];

        foreach (TrackInfo track in queue)
        {
            if (!byRequester.TryGetValue(track.RequesterId, out List<TrackInfo>? lane))
            {
                lane = [];
                byRequester[track.RequesterId] = lane;
                lanes.Add(lane);
            }

            lane.Add(track);
        }

        if (lanes.Count == 1)
        {
            return queue;
        }

        var ordered = new List<TrackInfo>(queue.Count);
        for (var round = 0; ordered.Count < queue.Count; round++)
        {
            foreach (List<TrackInfo> lane in lanes)
            {
                if (round < lane.Count)
                {
                    ordered.Add(lane[round]);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// Positions (1-based) to remove so each uri appears once, keeping the earliest copy.
    /// Descending, so a caller can remove them one by one without re-indexing
    /// (<c>/duplicate-cleanup</c>).
    /// </summary>
    public static IReadOnlyList<int> DuplicatePositions(IReadOnlyList<TrackInfo> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<int> duplicates = [];

        for (var i = 0; i < queue.Count; i++)
        {
            if (!seen.Add(queue[i].Uri))
            {
                duplicates.Add(i + 1);
            }
        }

        duplicates.Reverse();
        return duplicates;
    }

    /// <summary>
    /// How many tracks play before <paramref name="userId"/>'s next one, or <c>null</c> when
    /// they have nothing queued (docs/07-commands.md: "tracks-until-yours").
    /// </summary>
    public static int? TracksUntil(IReadOnlyList<TrackInfo> queue, ulong userId)
    {
        ArgumentNullException.ThrowIfNull(queue);

        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i].RequesterId == userId)
            {
                return i;
            }
        }

        return null;
    }
}
