namespace Sonarr.Domain.Levels;

/// <summary>
/// The guild's levels settings, read through <c>IGuildConfigService</c> (<c>/config levels …</c>).
/// Defaults are what a guild that has configured nothing gets.
/// </summary>
/// <param name="LevelupChannelId">Where level-ups are announced; <c>null</c> means "in the channel it happened".</param>
/// <param name="AnnounceByDm">Level-ups go to the user's DMs instead of a channel.</param>
/// <param name="XpMultiplier">Event multiplier, 1-5.</param>
/// <param name="DecayEnabled">Whether inactive members lose XP over time.</param>
/// <param name="ChannelWeights">Per-channel XP weight in percent; a channel that is absent is 100.</param>
public sealed record LevelsPolicy(
    ulong? LevelupChannelId,
    bool AnnounceByDm,
    int XpMultiplier,
    bool DecayEnabled,
    IReadOnlyDictionary<ulong, int> ChannelWeights)
{
    public static readonly LevelsPolicy Default = new(
        LevelupChannelId: null,
        AnnounceByDm: false,
        XpMultiplier: 1,
        DecayEnabled: false,
        ChannelWeights: new Dictionary<ulong, int>());

    /// <summary>Weight for one channel, in percent. Unweighted channels earn the normal rate.</summary>
    public int WeightFor(ulong channelId)
        => ChannelWeights.TryGetValue(channelId, out var weight) ? weight : 100;
}

/// <summary>
/// The two levels-only config keys. Named here rather than in
/// <c>Sonarr.Domain.Configuration.ConfigKeys</c> because the parser lives with them; the catalog
/// references these constants, so the reader and the validator cannot drift.
/// </summary>
public static class LevelsConfigKeys
{
    /// <summary>Boolean. Inactive-XP decay switch.</summary>
    public const string XpDecay = "xp_decay";

    /// <summary>
    /// Channel weights as <c>channelId:percent</c> pairs, comma separated
    /// (<c>123:150,456:0</c>). One key keeps the closed catalog closed.
    /// </summary>
    public const string XpChannelWeights = "xp_channel_weights";

    /// <summary>
    /// The accepted percent range for one channel weight. Named because three places need it: this
    /// parser, the catalog entry's bounds (and so the error text), and the <c>/config levels</c>
    /// slash-command bound. It was a bare <c>0 and &lt;= 500</c> here, which meant each of the
    /// others hardcoded the same pair and would have drifted the day someone loosened it.
    /// </summary>
    public const int MinWeightPercent = 0;

    /// <inheritdoc cref="MinWeightPercent"/>
    public const int MaxWeightPercent = 500;

    /// <summary>Parses the stored weights string. Unparsable pairs are skipped, never thrown.</summary>
    public static IReadOnlyDictionary<ulong, int> ParseWeights(string? raw)
    {
        var weights = new Dictionary<ulong, int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return weights;
        }

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':', 2);
            if (parts.Length == 2
                && ulong.TryParse(parts[0], out var channelId)
                && int.TryParse(parts[1], out var percent)
                && percent >= MinWeightPercent
                && percent <= MaxWeightPercent)
            {
                weights[channelId] = percent;
            }
        }

        return weights;
    }
}
