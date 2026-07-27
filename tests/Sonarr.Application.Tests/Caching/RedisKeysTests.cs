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
