using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Moderation;

/// <summary>
/// Turns a <see cref="PurgeFilter"/> plus a channel page into the exact set of message ids a
/// real run would delete. Pure and side-effect free by design: the preview and the live run use
/// the same function, so "preview showed 12" and "the run deleted 12" cannot disagree.
/// </summary>
public static class PurgePlanner
{
    /// <summary>
    /// Evaluates the filter. <paramref name="candidates"/> may be in any order; the result is
    /// oldest-first, which is the order Discord's bulk delete prefers.
    /// </summary>
    /// <param name="now">Injected so the 14-day window is testable. Defaults to UTC now.</param>
    public static PurgePreview Plan(
        PurgeFilter filter,
        IReadOnlyList<PurgeCandidate> candidates,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(candidates);

        DateTimeOffset cutoff = (now ?? DateTimeOffset.UtcNow) - PurgeFilter.BulkDeleteWindow;

        var pinned = 0;
        var tooOld = 0;
        List<PurgeCandidate> matched = [];

        // Newest first, so "count" means "the last N messages" the way a mod expects.
        foreach (PurgeCandidate candidate in candidates.OrderByDescending(c => c.MessageId))
        {
            if (matched.Count >= filter.Count)
            {
                break;
            }

            if (!Matches(filter, candidate))
            {
                continue;
            }

            // Pins are somebody's deliberate choice; a purge does not get to override that.
            if (candidate.IsPinned)
            {
                pinned++;
                continue;
            }

            if (candidate.CreatedAt < cutoff)
            {
                tooOld++;
                continue;
            }

            matched.Add(candidate);
        }

        Dictionary<ulong, int> breakdown = matched
            .GroupBy(m => m.AuthorId)
            .ToDictionary(g => g.Key, g => g.Count());

        return new PurgePreview(
            Matched: matched.Count,
            Deleted: 0,
            TooOld: tooOld,
            Pinned: pinned,
            MessageIds: [.. matched.OrderBy(m => m.MessageId).Select(m => m.MessageId)],
            AuthorBreakdown: breakdown);
    }

    private static bool Matches(PurgeFilter filter, PurgeCandidate candidate)
    {
        if (filter.FromUserId is { } author && candidate.AuthorId != author)
        {
            return false;
        }

        if (filter.BotsOnly && !candidate.AuthorIsBot)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(filter.Contains)
               || candidate.Content.Contains(filter.Contains.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
