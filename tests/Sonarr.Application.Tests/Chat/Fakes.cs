using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Chat;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Chat;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Elaine.Determinism;
using Sonarr.Elaine.Persona;

namespace Sonarr.Application.Tests.Chat;

/// <summary>
/// In-memory chat.person + facts/episodes/events. Records every write so a test can assert the
/// commit happened, and how many times.
/// </summary>
internal sealed class FakePersonRepository : IPersonRepository
{
    /// <summary>Same numbers as PersonRepository — a fake that drifts from them proves nothing.</summary>
    private const float InitialConfidence = 0.6f;

    private const float ReinforceStep = 0.15f;

    private readonly Dictionary<(long GuildId, long UserId), Person> _people = [];
    private readonly List<Fact> _facts = [];

    public List<ChatTurnWrite> Writes { get; } = [];

    public List<Episode> Episodes { get; } = [];

    public List<RelationshipEvent> Events { get; } = [];

    /// <summary>Next SaveTurnAsync throws, standing in for a failed transaction.</summary>
    public bool FailNextSave { get; set; }

    public Person Seed(long guildId, long userId, Action<Person>? setup = null)
    {
        Person row = new()
        {
            GuildId = guildId,
            UserId = userId,
            DialogueState = "idle",
            RelationshipTier = string.Empty,
        };
        setup?.Invoke(row);
        _people[(guildId, userId)] = row;
        return row;
    }

    /// <summary>One stored register movement, for the trajectory queries /relationship reads.</summary>
    public void SeedEvent(long guildId, long userId, double trust, DateTimeOffset at, string cause = "TEST")
        => Events.Add(new RelationshipEvent
        {
            GuildId = guildId,
            UserId = userId,
            Delta = new JsonObject { [Sonarr.Elaine.Conversation.Registers.Names.Trust] = trust },
            Cause = cause,
            At = at,
        });

    public Fact SeedFact(long guildId, long userId, string predicate, string value, long turn = 1)
    {
        Fact fact = new()
        {
            GuildId = guildId,
            UserId = userId,
            Predicate = predicate,
            Value = value,
            Confidence = 1f,
            LearnedAtTurn = turn,
            LearnedAt = Build.Now,
        };
        _facts.Add(fact);
        return fact;
    }

    public Task<Person?> GetAsync(long guildId, long userId, CancellationToken ct = default)
        => Task.FromResult(_people.GetValueOrDefault((guildId, userId)));

    public Task SaveTurnAsync(ChatTurnWrite write, CancellationToken ct = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new InvalidOperationException("transaction failed");
        }

        Writes.Add(write);
        _people[(write.Person.GuildId, write.Person.UserId)] = write.Person;
        Episodes.AddRange(write.Episodes);
        Events.AddRange(write.Events);

        if (write.Stance is { } stance)
        {
            // Same last-word-wins upsert as PersonRepository: one row per topic, replaced when
            // you change your mind.
            _stances.RemoveAll(a => a.GuildId == write.Person.GuildId
                && a.UserId == write.Person.UserId
                && a.Topic == stance.Topic);
            _stances.Add(new StanceAgreement
            {
                GuildId = write.Person.GuildId,
                UserId = write.Person.UserId,
                Topic = stance.Topic,
                Agreed = stance.Agreed,
                UpdatedAt = Build.Now,
            });
        }

        foreach (FactWrite fact in write.Facts)
        {
            Fact? existing = _facts.Find(f =>
                f.GuildId == write.Person.GuildId
                && f.UserId == write.Person.UserId
                && f.Predicate == fact.Predicate);

            // Mirrors PersonRepository.UpsertFactAsync: repeat mention reinforces, a new value
            // replaces at the starting confidence. A fake that stored 0 would make every fact
            // look shaky and every reply hedge.
            if (existing is not null && existing.Value == fact.Value)
            {
                existing.Confidence = Math.Min(1f, existing.Confidence + ReinforceStep);
                existing.LearnedAtTurn = fact.LearnedAtTurn;
                continue;
            }

            _facts.Remove(existing!);
            _facts.Add(new Fact
            {
                GuildId = write.Person.GuildId,
                UserId = write.Person.UserId,
                Predicate = fact.Predicate,
                Value = fact.Value,
                Confidence = InitialConfidence,
                LearnedAtTurn = fact.LearnedAtTurn,
            });
        }

        return Task.CompletedTask;
    }

    // Newest first, like the real repository — /memories renders in this order and truncates
    // the tail, so the order is part of the contract.
    public Task<IReadOnlyList<Fact>> GetFactsAsync(long guildId, long userId, CancellationToken ct = default)
    {
        FactReads++;
        return Task.FromResult<IReadOnlyList<Fact>>(
            [.. _facts
                .Where(f => f.GuildId == guildId && f.UserId == userId)
                .OrderByDescending(f => f.LearnedAtTurn)]);
    }

    /// <summary>How many times the fact table was read — the hedge must not add one per turn.</summary>
    public int FactReads { get; private set; }

    public Task<bool> ForgetFactAsync(long guildId, long userId, string predicate, CancellationToken ct = default)
        => Task.FromResult(_facts.RemoveAll(f =>
            f.GuildId == guildId && f.UserId == userId && f.Predicate == predicate) > 0);

    /// <remarks>Same trust-0 exclusion as the SQL: a stranger tops neither list.</remarks>
    public Task<IReadOnlyList<long>> GetTrustRankedUsersAsync(
        long guildId, int limit, bool lowestFirst = false, CancellationToken ct = default)
    {
        TrustRankReads++;
        IEnumerable<Person> people = _people.Values.Where(p => p.GuildId == guildId);
        return Task.FromResult<IReadOnlyList<long>>(
            [.. (lowestFirst
                    ? people.Where(p => p.Registers.Trust < 0).OrderBy(p => p.Registers.Trust)
                    : people.Where(p => p.Registers.Trust > 0).OrderByDescending(p => p.Registers.Trust))
                .Select(p => p.UserId)
                .Take(limit)]);
    }

    /// <summary>How many times the ordering was queried — a neutral person must not cost one.</summary>
    public int TrustRankReads { get; private set; }

    /// <remarks>Last word wins per topic, like the real upsert — one row, not a history.</remarks>
    public Task<IReadOnlyList<StanceAgreement>> GetStanceAgreementsAsync(
        long guildId, long userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StanceAgreement>>(
            [.. _stances
                .Where(a => a.GuildId == guildId && a.UserId == userId)
                .OrderByDescending(a => a.UpdatedAt)]);

    private readonly List<StanceAgreement> _stances = [];

    /// <summary>A side already taken, for the read half (/opinion) without replaying a turn.</summary>
    public void SeedStance(long guildId, long userId, string topic, bool agreed)
        => _stances.Add(new StanceAgreement
        {
            GuildId = guildId,
            UserId = userId,
            Topic = topic,
            Agreed = agreed,
            UpdatedAt = Build.Now,
        });

    public Task<IReadOnlyList<RelationshipEvent>> GetRecentEventsAsync(
        long guildId, long userId, DateTimeOffset since, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RelationshipEvent>>(
            [.. Events
                .Where(e => e.GuildId == guildId && e.UserId == userId && e.At >= since)
                .OrderBy(e => e.At)
                .Take(limit)]);
}

/// <summary>
/// In-memory session cache. Round-trips the hot person through JSON like Redis does, so a test
/// catches a snapshot the real cache could not serialize.
/// </summary>
internal sealed class FakeSessionCache : ISessionCache
{
    private readonly Dictionary<string, string> _hot = [];
    private readonly Dictionary<string, ChatSessionState> _sessions = [];
    private readonly Dictionary<string, string> _pending = [];
    private readonly Dictionary<string, string> _replied = [];
    private readonly Dictionary<string, string> _lastPing = [];
    private readonly Dictionary<ulong, List<RecentMessage>> _ring = [];
    private readonly Dictionary<ulong, int> _engagement = [];

    public IReadOnlyDictionary<ulong, int> Engagement => _engagement;

    public Task<ChatSessionState?> GetSessionAsync(ulong g, ulong u, CancellationToken ct = default)
        => Task.FromResult(_sessions.GetValueOrDefault(Key(g, u)));

    public Task<ChatSessionState> TouchSessionAsync(ulong g, ulong u, DateTimeOffset now, CancellationToken ct = default)
    {
        ChatSessionState? existing = _sessions.GetValueOrDefault(Key(g, u));
        ChatSessionState next = existing is null
            ? new ChatSessionState(1, now, now)
            : existing with { MessageCount = existing.MessageCount + 1, LastTurnAt = now };
        _sessions[Key(g, u)] = next;
        return Task.FromResult(next);
    }

    public void SeedSession(ulong g, ulong u, ChatSessionState state) => _sessions[Key(g, u)] = state;

    public Task SetPendingQuestionAsync(ulong g, ulong u, string questionId, CancellationToken ct = default)
    {
        _pending[Key(g, u)] = questionId;
        return Task.CompletedTask;
    }

    public Task<string?> GetPendingQuestionAsync(ulong g, ulong u, CancellationToken ct = default)
        => Task.FromResult(_pending.GetValueOrDefault(Key(g, u)));

    public Task ClearPendingQuestionAsync(ulong g, ulong u, CancellationToken ct = default)
    {
        _pending.Remove(Key(g, u));
        return Task.CompletedTask;
    }

    public Task MarkRepliedAsync(ulong c, ulong m, string stateHash, CancellationToken ct = default)
    {
        _replied[Key(c, m)] = stateHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetRepliedStateHashAsync(ulong c, ulong m, CancellationToken ct = default)
        => Task.FromResult(_replied.GetValueOrDefault(Key(c, m)));

    public Task PushRecentMessageAsync(ulong c, RecentMessage message, CancellationToken ct = default)
    {
        List<RecentMessage> ring = _ring.TryGetValue(c, out List<RecentMessage>? existing) ? existing : (_ring[c] = []);
        ring.Insert(0, message);
        if (ring.Count > 10)
        {
            ring.RemoveRange(10, ring.Count - 10);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecentMessage>> GetRecentMessagesAsync(ulong c, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecentMessage>>(_ring.GetValueOrDefault(c) ?? []);

    public Task<int> IncrementEngagementAsync(ulong c, CancellationToken ct = default)
        => Task.FromResult(_engagement[c] = _engagement.GetValueOrDefault(c) + 1);

    public Task<int> GetEngagementAsync(ulong c, CancellationToken ct = default)
        => Task.FromResult(_engagement.GetValueOrDefault(c));

    public void SeedEngagement(ulong c, int count) => _engagement[c] = count;

    public Task<string?> GetLastPingHashAsync(ulong g, ulong u, CancellationToken ct = default)
        => Task.FromResult(_lastPing.GetValueOrDefault(Key(g, u)));

    public Task SetLastPingHashAsync(ulong g, ulong u, string contentHash, CancellationToken ct = default)
    {
        _lastPing[Key(g, u)] = contentHash;
        return Task.CompletedTask;
    }

    public Task<TPerson?> GetHotPersonAsync<TPerson>(ulong g, ulong u, CancellationToken ct = default)
        where TPerson : class
        => Task.FromResult(_hot.TryGetValue(Key(g, u), out string? json)
            ? JsonSerializer.Deserialize<TPerson>(json, Json)
            : null);

    public Task SetHotPersonAsync<TPerson>(ulong g, ulong u, TPerson person, CancellationToken ct = default)
        where TPerson : class
    {
        _hot[Key(g, u)] = JsonSerializer.Serialize(person, Json);
        return Task.CompletedTask;
    }

    public bool HasHotPerson(ulong g, ulong u) => _hot.ContainsKey(Key(g, u));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Key(ulong a, ulong b) => $"{a}:{b}";
}

/// <summary>Every feature on unless a test turns one off.</summary>
internal sealed class FakeFeatureGate : IFeatureGate
{
    private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

    public FakeFeatureGate Off(string feature)
    {
        _states[feature] = false;
        return this;
    }

    public Task<bool> IsEnabledAsync(string feature, ulong guildId, CancellationToken ct = default)
        => Task.FromResult(_states.GetValueOrDefault(feature, true));

    public Task<IReadOnlyList<FeatureState>> GetAllAsync(ulong guildId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeatureState>>(
            [.. _states.Select(s => new FeatureState(s.Key, s.Value, FeatureStateSource.Guild))]);

    public Task<ConfigWriteResult> SetAsync(
        string feature, ulong guildId, bool enabled, ulong actorId, CancellationToken ct = default)
    {
        _states[feature] = enabled;
        return Task.FromResult(ConfigWriteResult.Ok(feature));
    }
}

internal static class Build
{
    public const ulong Guild = 111UL;
    public const ulong Channel = 222UL;
    public const ulong User = 333UL;
    public const ulong Message = 444UL;

    public static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The shipped persona, loaded once — the pipeline is only meaningful against real pools.</summary>
    public static PersonaGraph Graph => LazyGraph.Value;

    public static PersonaHolder Persona() => LazyHolder.Value;

    public static ChatPipeline Pipeline(
        FakePersonRepository? people = null,
        FakeSessionCache? cache = null,
        FakeFeatureGate? features = null,
        DateTimeOffset? now = null)
        => new(
            Persona(),
            people ?? new FakePersonRepository(),
            cache ?? new FakeSessionCache(),
            features ?? new FakeFeatureGate(),
            new FixedClock(now ?? Now),
            NullLogger<ChatPipeline>.Instance);

    public static ChatIntrospection Introspection(
        FakePersonRepository people, DateTimeOffset? now = null)
        => new(Persona(), people, new FixedClock(now ?? Now));

    public static ChatEditWatcher EditWatcher(
        FakeSessionCache cache,
        FakeFeatureGate? features = null)
        => new(Persona(), cache, features ?? new FakeFeatureGate(), NullLogger<ChatEditWatcher>.Instance);

    public static ChatRequest Request(string text, bool addressed = true, ulong message = Message)
        => new(Guild, Channel, User, message, text, addressed);

    /// <summary>Walks up to the repo root's persona directory, like the Elaine tests do.</summary>
    private static string PersonaDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "persona");
            if (File.Exists(Path.Combine(candidate, PersonaLoader.RootFile)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the persona/ directory");
    }

    private static readonly Lazy<PersonaHolder> LazyHolder = new(() =>
        PersonaHolder.TryCreate(new FilePersonaSource(PersonaDirectory()), out PersonaValidationResult result)
            ?? throw new InvalidOperationException("the shipped persona does not validate:\n" + result.Report()));

    private static readonly Lazy<PersonaGraph> LazyGraph = new(() => LazyHolder.Value.Current);
}

/// <summary>Reads persona files off disk. Test-only.</summary>
internal sealed class FilePersonaSource(string root) : IPersonaSource
{
    public IReadOnlyList<PersonaFile> Read() =>
        [.. Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new PersonaFile(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText(path)))];
}
