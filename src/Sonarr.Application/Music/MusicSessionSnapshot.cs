using Sonarr.Domain.Music;

namespace Sonarr.Application.Music;

/// <summary>
/// What <c>MusicSessionSnapshotter</c> writes to <c>music:session:{guild}</c> every 30 s and
/// <c>/resume</c> reads back (docs/08-background-services.md, docs/05-caching.md).
/// </summary>
/// <remarks>
/// Plain properties with a parameterless constructor on purpose: this round-trips through
/// <c>System.Text.Json</c> in the Redis cache, and a shape change must degrade to "can't resume",
/// which is exactly what a failed deserialize already does there.
/// </remarks>
public sealed class MusicSessionSnapshot
{
    public ulong VoiceChannelId { get; set; }

    public ulong TextChannelId { get; set; }

    /// <summary>The track that was playing. <c>null</c> means there is nothing to resume.</summary>
    public TrackInfo? Current { get; set; }

    public long PositionMs { get; set; }

    public int Volume { get; set; } = MusicRules.DefaultVolume;

    public LoopMode Loop { get; set; }

    public IReadOnlyList<TrackInfo> Queue { get; set; } = [];

    public DateTimeOffset SavedAt { get; set; }
}
