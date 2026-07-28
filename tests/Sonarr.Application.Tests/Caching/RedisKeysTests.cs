using Sonarr.Infrastructure.Caching;

namespace Sonarr.Application.Tests.Caching;

/// <summary>
/// Key-format and TTL-policy tests. No live Redis: these guard the contract in docs/05-caching.md,
/// which is the part that silently breaks (a renamed key = a silently empty cache).
/// </summary>
public class RedisKeysTests
{
    [Theory]
    // chat
    [InlineData("sonarr:chat:session:1:2")]
    [InlineData("sonarr:chat:pending_q:1:2")]
    [InlineData("sonarr:chat:replied:3:4")]
    [InlineData("sonarr:chat:ring:3")]
    [InlineData("sonarr:chat:budget:3")]
    [InlineData("sonarr:chat:lastping:1:2")]
    [InlineData("sonarr:chat:hot:1:2")]
    // music
    [InlineData("sonarr:music:session:1")]
    [InlineData("sonarr:music:voteskip:1")]
    [InlineData("sonarr:music:undo_skip:1")]
    [InlineData("sonarr:music:np_msg:1")]
    // rl
    [InlineData("sonarr:rl:cmd:2")]
    [InlineData("sonarr:rl:spam:1:2")]
    [InlineData("sonarr:rl:login:203.0.113.7")]
    [InlineData("sonarr:rl:xp:1:2")]
    // presence
    [InlineData("sonarr:presence:voice:1:2")]
    [InlineData("sonarr:presence:online_sample:1")]
    // web
    [InlineData("sonarr:web:session:abc123")]
    [InlineData("sonarr:web:live_status")]
    // cfg
    [InlineData("sonarr:cfg:guild:1")]
    [InlineData("sonarr:cfg:flags")]
    public void EveryKey_HasExpectedFormat(string expected) => Assert.Contains(expected, AllKeys());

    [Fact]
    public void AllKeys_UseTheSonarrAreaPrefix()
    {
        var areas = new[] { "chat", "music", "rl", "presence", "web", "cfg" };
        Assert.All(AllKeys(), key =>
        {
            var parts = key.Split(':');
            Assert.Equal("sonarr", parts[0]);
            Assert.Contains(parts[1], areas);
        });
    }

    [Fact]
    public void KeyBuilders_ProduceDistinctKeys()
    {
        var keys = AllKeys();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void LoginLimit_IsThreePerFifteenMinutes()
    {
        Assert.Equal(3, CacheTtl.LoginRequestsPerWindow);
        Assert.Equal(TimeSpan.FromMinutes(15), CacheTtl.RateLimitLogin);
    }

    [Fact]
    public void RingBuffer_IsCappedAtTen() => Assert.Equal(10, CacheTtl.RingBufferLength);

    [Theory]
    [InlineData(10 * 60, nameof(CacheTtl.ChatSession))]
    [InlineData(10 * 60, nameof(CacheTtl.ChatPendingQuestion))]
    [InlineData(60 * 60, nameof(CacheTtl.ChatReplied))]
    [InlineData(15 * 60, nameof(CacheTtl.ChatRing))]
    [InlineData(60 * 60, nameof(CacheTtl.ChatBudget))]
    [InlineData(5 * 60, nameof(CacheTtl.ChatLastPing))]
    [InlineData(5 * 60, nameof(CacheTtl.ChatHotPerson))]
    [InlineData(24 * 60 * 60, nameof(CacheTtl.MusicSession))]
    [InlineData(10, nameof(CacheTtl.MusicUndoSkip))]
    [InlineData(10, nameof(CacheTtl.RateLimitCommand))]
    [InlineData(5 * 60, nameof(CacheTtl.RateLimitSpam))]
    [InlineData(60, nameof(CacheTtl.RateLimitXp))]
    [InlineData(10 * 60, nameof(CacheTtl.PresenceOnlineSample))]
    [InlineData(5, nameof(CacheTtl.WebLiveStatus))]
    [InlineData(10 * 60, nameof(CacheTtl.ConfigGuild))]
    [InlineData(60, nameof(CacheTtl.ConfigFlags))]
    public void Ttl_MatchesDocumentedPolicy(int expectedSeconds, string field)
    {
        var value = (TimeSpan)typeof(CacheTtl).GetField(field)!.GetValue(null)!;
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), value);
    }

    [Fact]
    public void EveryTtl_IsPositive()
    {
        var ttls = typeof(CacheTtl).GetFields()
            .Where(f => f.FieldType == typeof(TimeSpan))
            .Select(f => ((TimeSpan)f.GetValue(null)!, f.Name))
            .ToList();

        Assert.NotEmpty(ttls);
        Assert.All(ttls, t => Assert.True(t.Item1 > TimeSpan.Zero, $"{t.Name} has a non-positive TTL"));
    }

    /// <summary>
    /// docs/05: a key without a TTL is a design bug. The write helper demands an expiry argument,
    /// which stops a key being written with none — but not a key family shipping with no TTL
    /// *policy*, which is how you end up with someone passing an arbitrary literal at the call site.
    /// So every builder in <see cref="RedisKeys"/> must have a same-named entry in
    /// <see cref="CacheTtl"/>. This is the audit at line 338, run on every build.
    /// </summary>
    [Fact]
    public void EveryKeyFamily_HasATtlPolicy()
    {
        var missing = KeyFamilies()
            .Where(name => typeof(CacheTtl).GetField(name) is null
                && typeof(CacheTtl).GetField(name + "Max") is null)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// The other half of the audit: a TTL nobody reads is a policy that has drifted from the key it
    /// claims to govern.
    /// </summary>
    [Fact]
    public void EveryTtl_BelongsToAKeyFamily()
    {
        var families = KeyFamilies();

        var orphans = typeof(CacheTtl).GetFields()
            .Where(f => f.FieldType == typeof(TimeSpan))
            .Select(f => f.Name)
            .Where(name => !families.Contains(name.Replace("Max", "", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(orphans);
    }

    /// <summary>
    /// docs/05 line 66: a full <c>FLUSHALL</c> must cost at most a reset conversation, an
    /// unresumable music session, and a panel re-login. Every key family therefore has to declare
    /// which of those three it is — and the point of the test is that adding a fourth kind of cost
    /// fails here, at the moment the key is added, rather than the morning after a flush.
    /// </summary>
    [Theory]
    [InlineData("ChatSession", Reset)]
    [InlineData("ChatPendingQuestion", Reset)]
    [InlineData("ChatReplied", Reset)]
    [InlineData("ChatRing", Reset)]
    [InlineData("ChatBudget", Reset)]
    [InlineData("ChatLastPing", Reset)]
    [InlineData("ChatHotPerson", Reset)]
    [InlineData("MusicSession", MusicStops)]
    [InlineData("MusicVoteSkip", MusicStops)]
    [InlineData("MusicUndoSkip", MusicStops)]
    [InlineData("MusicNowPlayingMessage", MusicStops)]
    // Limiters and cooldowns: a flush forgives whoever was mid-window. That is a reset, not data
    // loss — and rl:login fails *closed* while Redis is down, so a flush cannot open the gate.
    [InlineData("RateLimitCommand", Reset)]
    [InlineData("RateLimitSpam", Reset)]
    [InlineData("RateLimitLogin", Reset)]
    // rl:login:verify also fails closed, so a flush forgives an attacker mid-guessing at the cost
    // of a token they still do not have — and the token itself lives in Postgres.
    [InlineData("RateLimitLoginVerify", Reset)]
    [InlineData("RateLimitXp", Reset)]
    // Presence is re-sampled within 5 min by PresenceSampler; a lost voice key costs at most one
    // accrual window, and stats.activity_sample already has the durable copy.
    [InlineData("PresenceVoice", Reset)]
    [InlineData("PresenceOnlineSample", Reset)]
    // The session mirror only; web.session rows are the authority, so this is a re-login at worst.
    [InlineData("WebSession", ReLogin)]
    [InlineData("WebLiveStatus", ReLogin)]
    // Config falls back to the Postgres row on a miss, so a flush costs one uncached read.
    [InlineData("ConfigGuild", Reset)]
    [InlineData("ConfigFlags", Reset)]
    public void EveryKeyFamily_DeclaresItsFlushCost(string family, string cost)
    {
        Assert.Contains(family, KeyFamilies());
        Assert.Contains(cost, (string[])[Reset, MusicStops, ReLogin]);
    }

    /// <summary>Nothing may be added without a declared cost above.</summary>
    [Fact]
    public void EveryKeyFamily_IsAccountedForInTheBlastRadius()
    {
        var declared = typeof(RedisKeysTests)
            .GetMethod(nameof(EveryKeyFamily_DeclaresItsFlushCost))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(d => (string)d.GetData(null!).First().First()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(KeyFamilies(), family => Assert.Contains(family, declared));
    }

    private const string Reset = "conversations feel reset";
    private const string MusicStops = "music session cannot resume";
    private const string ReLogin = "panel re-login";

    /// <summary>Every public key builder's name — methods and get-only properties alike.</summary>
    private static HashSet<string> KeyFamilies()
        => [.. typeof(RedisKeys).GetMembers()
            .Where(m => m is System.Reflection.MethodInfo { IsStatic: true, IsSpecialName: false }
                or System.Reflection.PropertyInfo)
            .Select(m => m.Name)
            .Where(n => n != nameof(RedisKeys.Prefix))];

    private static List<string> AllKeys() =>
    [
        RedisKeys.ChatSession(1, 2),
        RedisKeys.ChatPendingQuestion(1, 2),
        RedisKeys.ChatReplied(3, 4),
        RedisKeys.ChatRing(3),
        RedisKeys.ChatBudget(3),
        RedisKeys.ChatLastPing(1, 2),
        RedisKeys.ChatHotPerson(1, 2),
        RedisKeys.MusicSession(1),
        RedisKeys.MusicVoteSkip(1),
        RedisKeys.MusicUndoSkip(1),
        RedisKeys.MusicNowPlayingMessage(1),
        RedisKeys.RateLimitCommand(2),
        RedisKeys.RateLimitSpam(1, 2),
        RedisKeys.RateLimitLogin("203.0.113.7"),
        RedisKeys.RateLimitXp(1, 2),
        RedisKeys.PresenceVoice(1, 2),
        RedisKeys.PresenceOnlineSample(1),
        RedisKeys.WebSession("abc123"),
        RedisKeys.WebLiveStatus,
        RedisKeys.ConfigGuild(1),
        RedisKeys.ConfigFlags,
    ];
}
