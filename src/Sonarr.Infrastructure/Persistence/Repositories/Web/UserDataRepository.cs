using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Infrastructure.Persistence.Repositories.Web;

/// <summary>
/// Reads and erases one user across every slice — the transparency page and the two delete
/// buttons of docs/06.
/// </summary>
public sealed class UserDataRepository(SonarrDbContext db) : IUserDataRepository
{
    public async Task<UserDataExport> ExportAsync(long userId, CancellationToken ct = default)
    {
        List<UserDataMembership> memberships =
        [
            .. await (from m in db.Members
                      join g in db.Guilds on m.GuildId equals g.GuildId
                      where m.UserId == userId
                      orderby m.GuildId
                      select new UserDataMembership(
                          m.GuildId,
                          g.Name,
                          m.Username,
                          m.DisplayName,
                          m.FirstSeenAt,
                          m.LastActiveAt,
                          m.MessageCount,
                          m.Timezone,
                          m.Birthday))
                .AsNoTracking()
                .ToListAsync(ct),
        ];

        List<UserDataLevels> levels =
        [
            .. await db.LevelProgress
                .Where(p => p.UserId == userId)
                .OrderBy(p => p.GuildId)
                // voice is stored in seconds; the page speaks minutes like /rank does
                .Select(p => new UserDataLevels(p.GuildId, p.Xp, p.Level, p.VoiceSeconds / 60, p.StreakDays))
                .AsNoTracking()
                .ToListAsync(ct),
        ];

        List<UserDataFact> facts =
        [
            .. await db.Facts
                .Where(f => f.UserId == userId && f.Active)
                .OrderBy(f => f.GuildId).ThenBy(f => f.Predicate)
                .Select(f => new UserDataFact(f.GuildId, f.Predicate, f.Value, f.Confidence, f.LearnedAt))
                .AsNoTracking()
                .ToListAsync(ct),
        ];

        UserDataCounts counts = new(
            ChatEpisodes: await db.Episodes.CountAsync(e => e.UserId == userId, ct),
            RelationshipEvents: await db.RelationshipEvents.CountAsync(r => r.UserId == userId, ct),
            TracksPlayed: await db.PlayHistory.CountAsync(h => h.RequesterId == userId, ct),
            TrackRatings: await db.TrackRatings.CountAsync(r => r.UserId == userId, ct),
            SavedQuotes: await db.Quotes.CountAsync(q => q.SavedBy == userId || q.AuthorId == userId, ct),
            Reminders: await PendingJobCountAsync(userId, ct),
            Capsules: await db.Capsules.CountAsync(c => c.AuthorId == userId, ct),
            ModCases: await db.Cases.CountAsync(c => c.TargetId == userId, ct));

        List<UserDataSession> sessions =
        [
            .. await db.WebSessions
                .Where(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > DateTimeOffset.UtcNow)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new UserDataSession(s.CreatedAt, s.ExpiresAt, s.UserAgent))
                .AsNoTracking()
                .ToListAsync(ct),
        ];

        return new UserDataExport(
            userId, DateTimeOffset.UtcNow, memberships, levels, facts, counts, sessions);
    }

    public async Task<UserDeletion> DeleteChatMemoryAsync(long userId, CancellationToken ct = default)
    {
        Dictionary<string, int> deleted = new(StringComparer.Ordinal)
        {
            ["chat.fact"] = await db.Facts.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct),
            ["chat.episode"] = await db.Episodes.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct),
            ["chat.relationship_event"] = await db.RelationshipEvents
                .Where(r => r.UserId == userId).ExecuteDeleteAsync(ct),
            ["chat.stance_agreement"] = await db.StanceAgreements
                .Where(s => s.UserId == userId).ExecuteDeleteAsync(ct),

            // last: the person row is the parent, and dropping it resets tier, registers,
            // nickname and the fired log in one go — she genuinely forgets.
            ["chat.person"] = await db.People.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct),
        };

        return new UserDeletion(deleted);
    }

    public async Task<UserDeletion> DeleteEverythingAsync(long userId, CancellationToken ct = default)
    {
        UserDeletion chat = await DeleteChatMemoryAsync(userId, ct);
        Dictionary<string, int> deleted = new(chat.Deleted, StringComparer.Ordinal)
        {
            ["levels.progress"] = await db.LevelProgress
                .Where(p => p.UserId == userId).ExecuteDeleteAsync(ct),
            ["levels.season_result"] = await db.SeasonResults
                .Where(r => r.UserId == userId).ExecuteDeleteAsync(ct),
            ["music.play_history"] = await db.PlayHistory
                .Where(h => h.RequesterId == userId).ExecuteDeleteAsync(ct),
            ["music.track_rating"] = await db.TrackRatings
                .Where(r => r.UserId == userId).ExecuteDeleteAsync(ct),
            ["music.user_prefs"] = await db.MusicUserPrefs
                .Where(p => p.UserId == userId).ExecuteDeleteAsync(ct),
            ["music.playlist"] = await db.Playlists
                .Where(p => p.OwnerId == userId).ExecuteDeleteAsync(ct),

            // quotes the user saved go; quotes of the user said elsewhere are someone else's
            // saved content, and docs/06 scopes deletion to what is yours.
            ["social.quote_board"] = await db.Quotes
                .Where(q => q.SavedBy == userId).ExecuteDeleteAsync(ct),
            ["social.capsule"] = await db.Capsules
                .Where(c => c.AuthorId == userId).ExecuteDeleteAsync(ct),
            ["social.event_rsvp"] = await db.EventRsvps
                .Where(r => r.UserId == userId).ExecuteDeleteAsync(ct),
            ["core.job"] = await DeletePendingJobsAsync(userId, ct),
            ["web.session"] = await db.WebSessions
                .Where(s => s.UserId == userId).ExecuteDeleteAsync(ct),
            ["web.login_token"] = await db.LoginTokens
                .Where(t => t.UserId == userId).ExecuteDeleteAsync(ct),
            ["core.member"] = await db.Members
                .Where(m => m.UserId == userId).ExecuteDeleteAsync(ct),
        };

        // mod.case survives on purpose (docs/06): a warning history a user could erase is not a
        // warning history. Say so rather than leaving the omission to be noticed.
        return new UserDeletion(deleted);
    }

    // payload is stored through a string converter, so ownership only matches in SQL — same
    // reason JobRepository does it this way. The key is JobKinds.UserIdField.
    private Task<int> PendingJobCountAsync(long userId, CancellationToken ct) =>
        db.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value" FROM core.job
            WHERE status = 'pending'
              AND payload->>'user_id' = {userId.ToString(CultureInfo.InvariantCulture)}
            """)
            .SingleAsync(ct);

    private Task<int> DeletePendingJobsAsync(long userId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM core.job
            WHERE status = 'pending'
              AND payload->>'user_id' = {userId.ToString(CultureInfo.InvariantCulture)}
            """,
            ct);
}
