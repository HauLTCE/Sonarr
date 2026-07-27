using Sonarr.Domain.Music;

namespace Sonarr.Application.Music;

/// <summary>
/// Picks the seed for smart autoplay: something this room actually liked, from what it has
/// played before (docs/checklist.md — "smart seeded from requester history, avoids net-negative
/// tracks"). Pure, so the "everything is disliked" and "no history" cases are testable.
/// </summary>
public static class AutoplaySeeder
{
    /// <summary>
    /// The best seed, or <c>null</c> when there is nothing worth seeding from — the caller then
    /// falls back to Lavalink's own related-track autoplay.
    /// </summary>
    /// <param name="history">Recent plays, most-played first.</param>
    /// <param name="dislikedUris">Net-negative uris; never seeded from.</param>
    /// <param name="excludeUris">Already queued or just played — avoid an immediate repeat.</param>
    public static string? PickSeed(
        IReadOnlyList<TrackPlayCount> history,
        IReadOnlyList<string> dislikedUris,
        IReadOnlyCollection<string>? excludeUris = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(dislikedUris);

        HashSet<string> blocked = new(dislikedUris, StringComparer.OrdinalIgnoreCase);
        if (excludeUris is not null)
        {
            blocked.UnionWith(excludeUris);
        }

        foreach (TrackPlayCount candidate in history)
        {
            if (!blocked.Contains(candidate.Uri))
            {
                return candidate.Uri;
            }
        }

        return null;
    }
}
