using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Infrastructure.Persistence.Repositories.Music;

/// <inheritdoc cref="IMusicStatsRepository"/>
public sealed class MusicStatsRepository(SonarrDbContext db) : IMusicStatsRepository
{
    public async Task RecordPlayAsync(ulong guildId, TrackInfo track, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        db.PlayHistory.Add(new PlayHistory
        {
            GuildId = (long)guildId,
            RequesterId = (long)track.RequesterId,
            Title = Truncate(track.Title, 512),
            Uri = Truncate(track.Uri, 1024),
            PlayedAt = DateTimeOffset.UtcNow,
            DurationMs = (int)Math.Clamp(track.DurationMs, 0, int.MaxValue),
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<MusicStats> GetStatsAsync(ulong guildId, int top, CancellationToken ct = default)
    {
        var guild = (long)guildId;
        var limit = Math.Clamp(top, 1, 25);

        IQueryable<PlayHistory> plays = db.PlayHistory.AsNoTracking().Where(h => h.GuildId == guild);

        var totals = await plays
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Ms = g.Sum(h => (long)h.DurationMs) })
            .FirstOrDefaultAsync(ct);

        if (totals is null)
        {
            return MusicStats.Empty;
        }

        List<TrackPlayCount> mostPlayed = await plays
            .GroupBy(h => new { h.Uri, h.Title })
            .Select(g => new TrackPlayCount(g.Key.Title, g.Key.Uri, g.Count()))
            .OrderByDescending(t => t.Plays)
            .Take(limit)
            .ToListAsync(ct);

        List<RequesterPlayCount> topRequesters = await plays
            .GroupBy(h => h.RequesterId)
            .Select(g => new { RequesterId = g.Key, Plays = g.Count() })
            .OrderByDescending(r => r.Plays)
            .Take(limit)
            .Select(r => new RequesterPlayCount((ulong)r.RequesterId, r.Plays))
            .ToListAsync(ct);

        return new MusicStats(
            totals.Count,
            TimeSpan.FromMilliseconds(totals.Ms),
            mostPlayed,
            topRequesters);
    }

    public async Task<IReadOnlyList<TrackPlayCount>> GetUserHistoryAsync(
        ulong guildId, ulong userId, int limit, CancellationToken ct = default)
    {
        var guild = (long)guildId;
        var requester = (long)userId;
        var take = Math.Clamp(limit, 1, 50);

        // Group by uri so a track played five times is one replayable row, ordered by the most
        // recent play — "my recent queue history, replayable" (docs/07-commands.md).
        return await db.PlayHistory
            .AsNoTracking()
            .Where(h => h.GuildId == guild && h.RequesterId == requester)
            .GroupBy(h => new { h.Uri, h.Title })
            .Select(g => new
            {
                g.Key.Title,
                g.Key.Uri,
                Plays = g.Count(),
                Last = g.Max(h => h.PlayedAt),
            })
            .OrderByDescending(x => x.Last)
            .Take(take)
            .Select(x => new TrackPlayCount(x.Title, x.Uri, x.Plays))
            .ToListAsync(ct);
    }

    public async Task<RatedTrack> RateAsync(
        ulong guildId,
        string uri,
        string title,
        ulong userId,
        short vote,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        if (vote is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(vote), vote, "A rating is +1 or -1.");
        }

        var guild = (long)guildId;
        var key = Truncate(uri, 1024);
        var user = (long)userId;

        TrackRating? row = await db.TrackRatings
            .FirstOrDefaultAsync(r => r.GuildId == guild && r.Uri == key && r.UserId == user, ct);

        if (row is null)
        {
            db.TrackRatings.Add(new TrackRating { GuildId = guild, Uri = key, UserId = user, Vote = vote });
        }
        else
        {
            row.Vote = vote;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return await GetRatingAsync(guildId, key, ct).ConfigureAwait(false)
               ?? new RatedTrack(title, key, 0, 0);
    }

    public async Task<RatedTrack?> GetRatingAsync(ulong guildId, string uri, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        var guild = (long)guildId;
        var key = Truncate(uri, 1024);

        var tally = await db.TrackRatings
            .AsNoTracking()
            .Where(r => r.GuildId == guild && r.Uri == key)
            .GroupBy(r => r.Uri)
            .Select(g => new
            {
                Likes = g.Count(r => r.Vote > 0),
                Dislikes = g.Count(r => r.Vote < 0),
            })
            .FirstOrDefaultAsync(ct);

        if (tally is null)
        {
            return null;
        }

        // track_rating stores no title (the uri is the identity); the caller supplies display text.
        return new RatedTrack(string.Empty, key, tally.Likes, tally.Dislikes);
    }

    public async Task<IReadOnlyList<RatedTrack>> GetTopRatedAsync(
        ulong guildId, int limit, CancellationToken ct = default)
    {
        var guild = (long)guildId;
        var take = Math.Clamp(limit, 1, 25);

        var rated = await db.TrackRatings
            .AsNoTracking()
            .Where(r => r.GuildId == guild)
            .GroupBy(r => r.Uri)
            .Select(g => new
            {
                Uri = g.Key,
                Likes = g.Count(r => r.Vote > 0),
                Dislikes = g.Count(r => r.Vote < 0),
            })
            .Where(x => x.Likes > x.Dislikes)
            .OrderByDescending(x => x.Likes - x.Dislikes)
            .ThenByDescending(x => x.Likes)
            .Take(take)
            .ToListAsync(ct);

        // Titles live in play_history; one extra round trip beats a title column that can drift.
        var uris = rated.ConvertAll(x => x.Uri);
        Dictionary<string, string> titles = await db.PlayHistory
            .AsNoTracking()
            .Where(h => h.GuildId == guild && uris.Contains(h.Uri))
            .GroupBy(h => h.Uri)
            .Select(g => new { Uri = g.Key, Title = g.Max(h => h.Title) })
            .ToDictionaryAsync(x => x.Uri, x => x.Title ?? string.Empty, ct);

        return rated.ConvertAll(x => new RatedTrack(
            titles.GetValueOrDefault(x.Uri, x.Uri),
            x.Uri,
            x.Likes,
            x.Dislikes));
    }

    public async Task<IReadOnlyList<string>> GetDislikedUrisAsync(ulong guildId, CancellationToken ct = default)
    {
        var guild = (long)guildId;

        return await db.TrackRatings
            .AsNoTracking()
            .Where(r => r.GuildId == guild)
            .GroupBy(r => r.Uri)
            .Where(g => g.Count(r => r.Vote < 0) > g.Count(r => r.Vote > 0))
            .Select(g => g.Key)
            .ToListAsync(ct);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
