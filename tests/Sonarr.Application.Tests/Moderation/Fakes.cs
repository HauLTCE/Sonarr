using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Core;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Tests.Moderation;

/// <summary>
/// In-memory mod.case. Numbers cases from 1 like the IDENTITY column does, so a test can assert
/// the case numbering the checklist asks for.
/// </summary>
internal sealed class FakeModCaseRepository : IModCaseRepository
{
    private readonly List<CaseRecord> _rows = [];
    private long _next;

    public IReadOnlyList<CaseRecord> Rows => _rows;

    public InfractionTally Tally { get; set; } = InfractionTally.Empty;

    public Task<CaseRecord> AddAsync(NewCase newCase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(newCase);

        CaseRecord record = new(
            ++_next,
            newCase.GuildId,
            newCase.TargetId,
            newCase.ActorId,
            newCase.Action,
            newCase.Reason,
            newCase.ExpiresAt,
            DateTimeOffset.UtcNow,
            newCase.Context ?? new Dictionary<string, string>());

        _rows.Add(record);
        return Task.FromResult(record);
    }

    public Task<CaseRecord?> GetAsync(long guildId, long caseId, CancellationToken ct = default)
        => Task.FromResult(_rows.FirstOrDefault(c => c.CaseId == caseId && (long)c.GuildId == guildId));

    public Task<CasePage> GetPageAsync(long guildId, long? targetId, int page, CancellationToken ct = default)
    {
        List<CaseRecord> matching =
        [
            .. _rows
                .Where(c => (long)c.GuildId == guildId)
                .Where(c => targetId is null || (long)c.TargetId == targetId)
                .OrderByDescending(c => c.CaseId),
        ];

        var wanted = Math.Max(page, 1);
        var pageCount = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)CasePage.PageSize));

        return Task.FromResult(new CasePage(
            [.. matching.Skip((wanted - 1) * CasePage.PageSize).Take(CasePage.PageSize)],
            wanted,
            pageCount,
            matching.Count));
    }

    public Task<InfractionTally> GetTallyAsync(long guildId, long targetId, CancellationToken ct = default)
        => Task.FromResult(Tally);
}

/// <summary>In-memory core.job. Keeps every scheduled row so a test can inspect run_at and payload.</summary>
internal sealed class FakeJobRepository : IJobRepository
{
    private long _next;

    public List<Job> Scheduled { get; } = [];

    public Task<long> ScheduleAsync(
        string kind, DateTimeOffset runAt, JsonObject payload, string? recurrence = null, CancellationToken ct = default)
    {
        var id = ++_next;
        Scheduled.Add(new Job
        {
            JobId = id,
            Kind = kind,
            RunAt = runAt,
            Payload = payload,
            Recurrence = recurrence,
        });

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<Job>> ClaimDueAsync(int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Job>>([]);

    public Task CompleteAsync(long jobId, DateTimeOffset? nextRunAt = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task FailAsync(long jobId, string error, CancellationToken ct = default) => Task.CompletedTask;

    public Task RetryAsync(
        long jobId, DateTimeOffset runAt, JsonObject payload, string? error, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Job>> GetPendingByKindAsync(string kind, long userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Job>>([.. Scheduled.Where(j => j.Kind == kind)]);

    public Task<bool> CancelAsync(long jobId, long userId, CancellationToken ct = default)
        => Task.FromResult(Scheduled.RemoveAll(j => j.JobId == jobId) > 0);
}

/// <summary>Config service stub: hands back whatever policy values a test seeds.</summary>
internal sealed class FakeGuildConfigService : IGuildConfigService
{
    private readonly Dictionary<string, ConfigValue> _values = [];

    public FakeGuildConfigService With(string key, string raw)
    {
        _values[key] = Value(key, raw);
        return this;
    }

    public Task<IReadOnlyDictionary<string, ConfigValue>> GetAllAsync(
        ulong guildId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, ConfigValue>>(_values);

    public Task<ConfigValue?> GetAsync(ulong guildId, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_values.GetValueOrDefault(key));

    public Task<ConfigWriteResult> SetAsync(
        ulong guildId, string key, string value, ulong actorId, CancellationToken cancellationToken = default)
    {
        _values[key] = Value(key, value);
        return Task.FromResult(ConfigWriteResult.Ok($"`{key}` set."));
    }

    public Task<ConfigWriteResult> ClearAsync(
        ulong guildId, string key, ulong actorId, CancellationToken cancellationToken = default)
    {
        _values.Remove(key);
        return Task.FromResult(ConfigWriteResult.Ok($"`{key}` cleared."));
    }

    public Task<string> ExportAsync(ulong guildId, CancellationToken cancellationToken = default)
        => Task.FromResult("{}");

    public Task<ConfigImportResult> ImportAsync(
        ulong guildId, string json, ulong actorId, CancellationToken cancellationToken = default)
        => Task.FromResult(ConfigImportResult.Ok(0));

    /// <summary>
    /// Moderation keys are not in <c>ConfigKeys.All</c> yet, so the definition is synthesised here.
    /// Only <c>Raw</c> matters to <c>ModerationConfig.ReadPolicy</c>.
    /// </summary>
    private static ConfigValue Value(string key, string raw)
        => new(new ConfigKeyDefinition(key, ConfigValueKind.Integer, key), raw);
}

/// <summary>
/// Records message hashes in memory the way <c>rl:spam:{guild}:{user}</c> does, so the
/// identical-flood counter can be driven without Redis.
/// </summary>
internal sealed class FakeCooldownStore : ICooldownStore
{
    private readonly Dictionary<(ulong Guild, ulong User, string Hash), int> _seen = [];

    /// <summary>Simulates Redis being down: the flood rule has to cope, not crash.</summary>
    public bool Unavailable { get; set; }

    public int Cleared { get; private set; }

    public Task<int> RecordMessageHashAsync(
        ulong guildId, ulong userId, string messageHash, CancellationToken ct = default)
    {
        if (Unavailable)
        {
            // Matches RedisCooldownStore: the flood counter fails open (1 = "first time seen").
            return Task.FromResult(1);
        }

        (ulong guildId, ulong userId, string messageHash) key = (guildId, userId, messageHash);
        var count = _seen.GetValueOrDefault(key) + 1;
        _seen[key] = count;
        return Task.FromResult(count);
    }

    public Task ClearMessageHashesAsync(ulong guildId, ulong userId, CancellationToken ct = default)
    {
        Cleared++;
        foreach ((ulong Guild, ulong User, string Hash) key in _seen.Keys
            .Where(k => k.Guild == guildId && k.User == userId)
            .ToList())
        {
            _seen.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryAcquireXpAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryAcquireCommandAsync(ulong userId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryConsumeLoginAsync(string identifier, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> TryConsumeVerifyAsync(string identifier, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal static class Build
{
    public const ulong Guild = 4001UL;
    public const ulong Actor = 4002UL;
    public const ulong Target = 4003UL;

    public static (ModerationService Service, FakeModCaseRepository Cases, FakeJobRepository Jobs)
        Moderation(FakeGuildConfigService? config = null)
    {
        FakeModCaseRepository cases = new();
        FakeJobRepository jobs = new();
        ModerationService service = new(
            cases, jobs, config ?? new FakeGuildConfigService(), NullLogger<ModerationService>.Instance);

        return (service, cases, jobs);
    }

    /// <summary>An actor who outranks the target and has the permission — the happy path.</summary>
    public static HierarchySnapshot Allowed(int actorRole = 10, int? targetRole = 5, int botRole = 20)
        => new(
            ActorHasPermission: true,
            BotHasPermission: true,
            ActorIsOwner: false,
            ActorTopRole: actorRole,
            BotTopRole: botRole,
            TargetTopRole: targetRole);

    public static NewCase Case(CaseAction action = CaseAction.Warn, string reason = "spam")
        => new(Guild, Target, Actor, action, reason);
}
