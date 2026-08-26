using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Levels;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Entities.Levels;
using Sonarr.Domain.Levels;

namespace Sonarr.Application.Tests.Levels;

/// <summary>In-memory levels.progress.</summary>
internal sealed class FakeLevelProgressRepository : ILevelProgressRepository
{
    private readonly Dictionary<(long GuildId, long UserId), LevelProgress> _rows = [];

    public int Saves { get; private set; }

    public LevelProgress Seed(long guildId, long userId, Action<LevelProgress> setup)
    {
        LevelProgress row = new() { GuildId = guildId, UserId = userId, Level = LevelCurve.FirstLevel };
        setup(row);
        _rows[(guildId, userId)] = row;
        return row;
    }

    public Task<LevelProgress?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => Task.FromResult(_rows.GetValueOrDefault((guildId, userId)));

    public Task<LevelProgress> GetOrCreateAsync(long guildId, long userId, CancellationToken ct = default)
    {
        if (!_rows.TryGetValue((guildId, userId), out LevelProgress? row))
        {
            row = new LevelProgress { GuildId = guildId, UserId = userId, Level = LevelCurve.FirstLevel };
            _rows[(guildId, userId)] = row;
        }

        return Task.FromResult(row);
    }

    public Task SaveAsync(LevelProgress progress, CancellationToken ct = default)
    {
        Saves++;
        _rows[(progress.GuildId, progress.UserId)] = progress;
        return Task.CompletedTask;
    }

    public async Task<LevelProgress> AddVoiceAsync(
        long guildId, long userId, long seconds, long xp, int newLevel, CancellationToken ct = default)
    {
        LevelProgress row = await GetOrCreateAsync(guildId, userId, ct);
        row.Xp += xp;
        row.VoiceSeconds += seconds;
        row.Level = newLevel;
        return row;
    }

    public Task<int> GetRankAsync(long guildId, long userId, CancellationToken ct = default)
    {
        if (!_rows.TryGetValue((guildId, userId), out LevelProgress? mine))
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(_rows.Values.Count(r => r.GuildId == guildId && r.Xp > mine.Xp) + 1);
    }

    public Task<int> CountAsync(long guildId, CancellationToken ct = default)
        => Task.FromResult(_rows.Values.Count(r => r.GuildId == guildId));

    public Task<IReadOnlyList<LeaderboardEntry>> GetTopAsync(
        long guildId, int skip, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(
            [.. _rows.Values
                .Where(r => r.GuildId == guildId)
                .OrderByDescending(r => r.Xp)
                .ThenBy(r => r.UserId)
                .Skip(skip)
                .Take(take)
                .Select((r, i) => new LeaderboardEntry(skip + i + 1, (ulong)r.UserId, r.Xp, r.Level))]);
}

/// <summary>In-memory levels.reward.</summary>
internal sealed class FakeLevelRewardRepository : ILevelRewardRepository
{
    private readonly Dictionary<(long GuildId, int Level), long> _rows = [];

    public FakeLevelRewardRepository With(long guildId, int level, long roleId)
    {
        _rows[(guildId, level)] = roleId;
        return this;
    }

    public Task<IReadOnlyList<LevelReward>> GetAllAsync(long guildId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LevelReward>>([.. Rows(guildId)]);

    public Task<IReadOnlyList<LevelReward>> GetEarnedAsync(long guildId, int level, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LevelReward>>([.. Rows(guildId).Where(r => r.Level <= level)]);

    public Task SetAsync(long guildId, int level, long roleId, CancellationToken ct = default)
    {
        _rows[(guildId, level)] = roleId;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(long guildId, int level, CancellationToken ct = default)
        => Task.FromResult(_rows.Remove((guildId, level)));

    private IEnumerable<LevelReward> Rows(long guildId)
        => _rows
            .Where(r => r.Key.GuildId == guildId)
            .OrderBy(r => r.Key.Level)
            .Select(r => new LevelReward { GuildId = guildId, Level = r.Key.Level, RoleId = r.Value });
}

/// <summary>In-memory levels.season. Empty unless a test seeds it.</summary>
internal sealed class FakeSeasonRepository : ISeasonRepository
{
    private readonly List<Season> _seasons = [];
    private readonly Dictionary<long, List<LeaderboardEntry>> _results = [];

    public FakeSeasonRepository With(long seasonId, long guildId, string status, params LeaderboardEntry[] results)
    {
        _seasons.Add(new Season
        {
            SeasonId = seasonId,
            GuildId = guildId,
            StartsAt = DateTimeOffset.UnixEpoch,
            EndsAt = DateTimeOffset.UnixEpoch.AddDays(30),
            Status = status,
        });
        _results[seasonId] = [.. results];
        return this;
    }

    /// <summary>
    /// Seeds an already-open season. Separate from <see cref="OpenAsync"/> so the arrange step does
    /// not show up in <see cref="Opened"/> as something the roller did.
    /// </summary>
    public Season Open(long guildId, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        Season season = new()
        {
            SeasonId = _seasons.Count + 1,
            GuildId = guildId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = SeasonStatus.Active,
        };

        _seasons.Add(season);
        return season;
    }

    public Task<IReadOnlyList<Season>> GetAllAsync(long guildId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Season>>(
            [.. _seasons.Where(s => s.GuildId == guildId).OrderByDescending(s => s.SeasonId)]);

    public Task<Season?> GetAsync(long seasonId, CancellationToken ct = default)
        => Task.FromResult(_seasons.Find(s => s.SeasonId == seasonId));

    public Task<Season?> GetActiveAsync(long guildId, CancellationToken ct = default)
        => Task.FromResult(_seasons.Find(s => s.GuildId == guildId && s.Status == SeasonStatus.Active));

    public Task<IReadOnlyList<LeaderboardEntry>> GetResultsAsync(
        long seasonId, int skip, int take, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(
            [.. _results.GetValueOrDefault(seasonId, []).Skip(skip).Take(take)]);

    public Task<int> CountResultsAsync(long seasonId, CancellationToken ct = default)
        => Task.FromResult(_results.GetValueOrDefault(seasonId, []).Count);

    // ---- The roller's half -------------------------------------------------------------------

    /// <summary>Standings the next close will see. Set by a test; empty means a quiet month.</summary>
    public List<SeasonStanding> Standings { get; } = [];

    /// <summary>Windows passed to <see cref="OpenAsync"/>, in order — one per roll.</summary>
    public List<(long GuildId, DateTimeOffset Starts, DateTimeOffset Ends)> Opened { get; } = [];

    public Task<IReadOnlyList<SeasonStanding>> GetStandingsAsync(
        long guildId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SeasonStanding>>([.. Standings]);

    public Task<bool> CloseAsync(
        long seasonId, IReadOnlyList<SeasonResult> results, CancellationToken ct = default)
    {
        // Mirrors the real WHERE status = 'active': a season already closed writes nothing.
        Season? season = _seasons.Find(s => s.SeasonId == seasonId && s.Status == SeasonStatus.Active);
        if (season is null)
        {
            return Task.FromResult(false);
        }

        season.Status = SeasonStatus.Closed;
        _results[seasonId] = [.. results.Select(r => new LeaderboardEntry(r.Rank, (ulong)r.UserId, r.XpEarned, 0))];
        return Task.FromResult(true);
    }

    public Task<Season> OpenAsync(
        long guildId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct = default)
    {
        Opened.Add((guildId, startsAt, endsAt));

        Season season = new()
        {
            SeasonId = _seasons.Count + 1,
            GuildId = guildId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = SeasonStatus.Active,
        };

        _seasons.Add(season);
        return Task.FromResult(season);
    }
}

/// <summary>In-memory core.member — only what /userstats reads.</summary>
internal sealed class FakeMemberRepository : IMemberRepository
{
    private readonly Dictionary<(long GuildId, long UserId), Member> _rows = [];

    public List<MemberActivityDelta> Applied { get; } = [];

    public Task<Member?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => Task.FromResult(_rows.GetValueOrDefault((guildId, userId)));

    /// <summary>Mirrors the real ON CONFLICT upsert, including first_seen_at set on insert only.</summary>
    public Task ApplyActivityAsync(IReadOnlyCollection<MemberActivityDelta> deltas, CancellationToken ct = default)
    {
        Applied.AddRange(deltas);
        foreach (MemberActivityDelta delta in deltas)
        {
            if (!_rows.TryGetValue((delta.GuildId, delta.UserId), out Member? row))
            {
                row = _rows[(delta.GuildId, delta.UserId)] = new Member
                {
                    GuildId = delta.GuildId,
                    UserId = delta.UserId,
                    FirstSeenAt = delta.JoinedAt ?? delta.LastActiveAt,
                };
            }

            row.MessageCount += delta.MessageCount;
            row.LastActiveAt = delta.LastActiveAt;

            if (delta.Username.Length > 0)
            {
                row.Username = delta.Username;
            }

            if (delta.DisplayName.Length > 0)
            {
                row.DisplayName = delta.DisplayName;
            }
        }

        return Task.CompletedTask;
    }

    public Task SetTimezoneAsync(long guildId, long userId, string? ianaTimezone, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>Adds a row directly, for tests that need one to already exist.</summary>
    public Member Seed(long guildId, long userId, DateTimeOffset firstSeenAt, DateOnly? birthday = null)
        => _rows[(guildId, userId)] = new Member
        {
            GuildId = guildId,
            UserId = userId,
            FirstSeenAt = firstSeenAt,
            Birthday = birthday,
        };

    public Task SetBirthdayAsync(long guildId, long userId, DateOnly? birthday, CancellationToken ct = default)
    {
        // Mirrors the real ExecuteUpdateAsync: no row, no write, no insert.
        if (_rows.TryGetValue((guildId, userId), out Member? row))
        {
            row.Birthday = birthday;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Member>> GetBirthdaysAsync(
        long guildId, int month, int day, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Member>>(
            [.. _rows.Values.Where(m => m.GuildId == guildId
                && m.Birthday is { } b && b.Month == month && b.Day == day)]);

    public Task<IReadOnlyList<Member>> GetJoinAnniversariesAsync(
        long guildId, int month, int day, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Member>>(
            [.. _rows.Values.Where(m => m.GuildId == guildId
                && m.FirstSeenAt.Month == month && m.FirstSeenAt.Day == day)]);

    /// <summary>Names a seeded row, so the username lookup has something to find.</summary>
    public FakeMemberRepository Named(long guildId, long userId, string username)
    {
        Member row = _rows.TryGetValue((guildId, userId), out Member? existing)
            ? existing
            : _rows[(guildId, userId)] = new Member { GuildId = guildId, UserId = userId };
        row.Username = username;
        return this;
    }

    public Task<long?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        // Mirrors the real query: distinct ids, ambiguous means nobody.
        long[] ids = [.. _rows.Values
            .Where(m => string.Equals(m.Username, username, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.UserId)
            .Distinct()];

        return Task.FromResult(ids.Length == 1 ? ids[0] : (long?)null);
    }

    public Task<IReadOnlyList<MemberGuild>> GetGuildsAsync(long userId, CancellationToken ct = default)
    {
        // No guild names in this fake — naming is the caller's problem, not the repository's, so
        // the id doubles as the name.
        IReadOnlyList<MemberGuild> guilds = [.. _rows.Values
            .Where(m => m.UserId == userId)
            .Select(m => new MemberGuild(m.GuildId, m.GuildId.ToString(CultureInfo.InvariantCulture)))];

        return Task.FromResult(guilds);
    }
}

/// <summary>
/// The XP cooldown gate. <see cref="Available"/> false is "Redis unreachable" — the store's
/// contract says that FAILS CLOSED, so it answers false and nobody gets XP.
/// </summary>
internal sealed class FakeCooldownStore : ICooldownStore
{
    private readonly HashSet<(ulong, ulong)> _held = [];

    public bool Available { get; set; } = true;

    public int Attempts { get; private set; }

    public Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken ct = default)
    {
        Attempts++;

        if (!Available)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_held.Add((guildId, userId)));
    }

    // The rest of the store belongs to other slices; levels only ever calls the XP window.
    public Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryConsumeLoginAsync(string identifier, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryConsumeVerifyAsync(string identifier, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<int> RecordMessageHashAsync(
        ulong guildId, ulong userId, string messageHash, CancellationToken cancellationToken = default)
        => Task.FromResult(1);

    public Task ClearMessageHashesAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Guild config as a flat key → raw-value map, no Postgres and no cache in the way.</summary>
internal sealed class FakeGuildConfigService : IGuildConfigService
{
    private readonly Dictionary<string, string> _values = [];

    public FakeGuildConfigService With(string key, string raw)
    {
        _values[key] = raw;
        return this;
    }

    public Task<ConfigValue?> GetAsync(ulong guildId, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Read(key));

    public Task<IReadOnlyDictionary<string, ConfigValue>> GetAllAsync(
        ulong guildId, CancellationToken cancellationToken = default)
    {
        Dictionary<string, ConfigValue> all = [];
        foreach ((var key, _) in _values)
        {
            if (Read(key) is { } value)
            {
                all[key] = value;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, ConfigValue>>(all);
    }

    public Task<ConfigWriteResult> SetAsync(
        ulong guildId, string key, string value, ulong actorId, CancellationToken cancellationToken = default)
    {
        _values[key] = value;
        return Task.FromResult(ConfigWriteResult.Ok("set"));
    }

    public Task<ConfigWriteResult> ClearAsync(
        ulong guildId, string key, ulong actorId, CancellationToken cancellationToken = default)
    {
        _values.Remove(key);
        return Task.FromResult(ConfigWriteResult.Ok("cleared"));
    }

    public Task<string> ExportAsync(ulong guildId, CancellationToken cancellationToken = default)
        => Task.FromResult("{}");

    public Task<ConfigImportResult> ImportAsync(
        ulong guildId, string json, ulong actorId, CancellationToken cancellationToken = default)
        => Task.FromResult(ConfigImportResult.Ok(0));

    /// <summary>
    /// The definition is only consulted for typed reads, and the levels service reads
    /// <see cref="ConfigValue.Raw"/> or one of the typed accessors — a String definition covers
    /// both, including the two keys that are not in the catalog yet.
    /// </summary>
    private ConfigValue? Read(string key)
    {
        if (!_values.TryGetValue(key, out var raw))
        {
            return null;
        }

        // The kind only drives validation on write, which the real service does before this point;
        // every ConfigValue accessor parses Raw and never consults the definition. So the stand-in
        // below is enough for the two keys that are not in the catalog yet.
        ConfigKeyDefinition definition = ConfigKeys.TryGet(key, out ConfigKeyDefinition? known)
            ? known
            : new ConfigKeyDefinition(key, ConfigValueKind.Boolean, key);

        return new ConfigValue(definition, raw);
    }
}

internal static class Build
{
    public const ulong Guild = 111UL;
    public const ulong User = 222UL;
    public const ulong Channel = 333UL;

    public static readonly DateOnly Today = new(2026, 7, 27);

    public static Fixture Levels(
        FakeLevelRewardRepository? rewards = null,
        FakeSeasonRepository? seasons = null,
        FakeGuildConfigService? config = null)
    {
        FakeLevelProgressRepository progress = new();
        FakeLevelRewardRepository reward = rewards ?? new FakeLevelRewardRepository();
        FakeSeasonRepository season = seasons ?? new FakeSeasonRepository();
        FakeMemberRepository members = new();
        FakeGuildConfigService settings = config ?? new FakeGuildConfigService();
        FakeCooldownStore cooldowns = new();

        LevelService service = new(
            progress, reward, season, members, settings, cooldowns,
            NullLogger<LevelService>.Instance);

        return new Fixture(service, progress, reward, season, members, settings, cooldowns);
    }

    internal sealed record Fixture(
        LevelService Service,
        FakeLevelProgressRepository Progress,
        FakeLevelRewardRepository Rewards,
        FakeSeasonRepository Seasons,
        FakeMemberRepository Members,
        FakeGuildConfigService Config,
        FakeCooldownStore Cooldowns);
}
