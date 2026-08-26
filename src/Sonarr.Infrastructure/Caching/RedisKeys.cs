namespace Sonarr.Infrastructure.Caching;

/// <summary>
/// Single source of truth for every Redis key and its TTL (docs/05-caching.md).
/// Convention: <c>sonarr:{area}:{...}</c>. No other file builds a key string by hand.
/// </summary>
/// <remarks>
/// Every key here has a TTL below it in <see cref="CacheTtl"/>: a key without a TTL is a
/// design bug, so the write helpers in <see cref="RedisCacheBase"/> require an expiry argument.
/// </remarks>
public static class RedisKeys
{
    public const string Prefix = "sonarr";

    // ---- area: chat ----------------------------------------------------------------
    public static string ChatSession(ulong guildId, ulong userId) => $"{Prefix}:chat:session:{guildId}:{userId}";

    public static string ChatPendingQuestion(ulong guildId, ulong userId) => $"{Prefix}:chat:pending_q:{guildId}:{userId}";

    public static string ChatReplied(ulong channelId, ulong messageId) => $"{Prefix}:chat:replied:{channelId}:{messageId}";

    public static string ChatRing(ulong channelId) => $"{Prefix}:chat:ring:{channelId}";

    public static string ChatBudget(ulong channelId) => $"{Prefix}:chat:budget:{channelId}";

    public static string ChatLastPing(ulong guildId, ulong userId) => $"{Prefix}:chat:lastping:{guildId}:{userId}";

    public static string ChatHotPerson(ulong guildId, ulong userId) => $"{Prefix}:chat:hot:{guildId}:{userId}";

    // ---- area: music ---------------------------------------------------------------
    public static string MusicSession(ulong guildId) => $"{Prefix}:music:session:{guildId}";

    public static string MusicVoteSkip(ulong guildId) => $"{Prefix}:music:voteskip:{guildId}";

    public static string MusicUndoSkip(ulong guildId) => $"{Prefix}:music:undo_skip:{guildId}";

    public static string MusicNowPlayingMessage(ulong guildId) => $"{Prefix}:music:np_msg:{guildId}";

    // ---- area: rl ------------------------------------------------------------------
    public static string RateLimitCommand(ulong userId) => $"{Prefix}:rl:cmd:{userId}";

    public static string RateLimitSpam(ulong guildId, ulong userId) => $"{Prefix}:rl:spam:{guildId}:{userId}";

    public static string RateLimitXp(ulong guildId, ulong userId) => $"{Prefix}:rl:xp:{guildId}:{userId}";

    // ---- area: presence -----------------------------------------------------------
    public static string PresenceVoice(ulong guildId, ulong userId) => $"{Prefix}:presence:voice:{guildId}:{userId}";

    public static string PresenceOnlineSample(ulong guildId) => $"{Prefix}:presence:online_sample:{guildId}";

    // ---- area: cfg ----------------------------------------------------------------
    public static string ConfigGuild(ulong guildId) => $"{Prefix}:cfg:guild:{guildId}";

    public static string ConfigFlags => $"{Prefix}:cfg:flags";
}

/// <summary>TTL policy table — one entry per key family in <see cref="RedisKeys"/>.</summary>
public static class CacheTtl
{
    // chat
    public static readonly TimeSpan ChatSession = TimeSpan.FromMinutes(10);      // sliding
    public static readonly TimeSpan ChatPendingQuestion = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ChatReplied = TimeSpan.FromHours(1);
    public static readonly TimeSpan ChatRing = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ChatBudget = TimeSpan.FromHours(1);          // fixed window
    public static readonly TimeSpan ChatLastPing = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ChatHotPerson = TimeSpan.FromMinutes(5);     // sliding

    // music
    public static readonly TimeSpan MusicSession = TimeSpan.FromHours(24);
    /// <summary>Safety net only: the tally is cleared on track end, this stops a lost event leaking a key.</summary>
    public static readonly TimeSpan MusicVoteSkip = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MusicUndoSkip = TimeSpan.FromSeconds(10);
    /// <summary>"Session" length — same envelope as the queue snapshot it belongs to.</summary>
    public static readonly TimeSpan MusicNowPlayingMessage = TimeSpan.FromHours(24);

    // rl
    public static readonly TimeSpan RateLimitCommand = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RateLimitSpam = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RateLimitXp = TimeSpan.FromSeconds(60);

    // presence
    /// <summary>Voice sessions end on leave; this is the sweep that survives a missed leave event.</summary>
    public static readonly TimeSpan PresenceVoice = TimeSpan.FromHours(12);
    public static readonly TimeSpan PresenceOnlineSample = TimeSpan.FromMinutes(10);

    // cfg
    public static readonly TimeSpan ConfigGuild = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ConfigFlags = TimeSpan.FromMinutes(1);

    /// <summary>Channel ring buffer cap (docs/05: list capped 10).</summary>
    public const int RingBufferLength = 10;
}
