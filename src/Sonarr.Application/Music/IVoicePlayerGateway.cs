namespace Sonarr.Application.Music;

/// <summary>
/// The narrow slice of the live audio player that <see cref="VoiceMoveCoordinator"/> needs.
/// Implemented in Sonarr.Bot over Lavalink4NET; faked in tests, which is the whole point —
/// the drag-and-reconnect rule is a decision, and decisions do not need a gateway to test
/// (docs/checklist.md — "Regression test for the VC-drag case").
/// </summary>
public interface IVoicePlayerGateway
{
    /// <summary>The channel the player is bound to, or <c>null</c> when there is no player.</summary>
    ValueTask<ulong?> GetPlayerChannelAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Re-establishes the voice connection on <paramref name="voiceChannelId"/>, keeping playback.</summary>
    ValueTask ReconnectAsync(ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default);

    /// <summary>Humans in the channel, bots excluded.</summary>
    ValueTask<int> CountListenersAsync(
        ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default);

    ValueTask<bool> IsPausedAsync(ulong guildId, CancellationToken cancellationToken = default);

    ValueTask PauseAsync(ulong guildId, CancellationToken cancellationToken = default);

    ValueTask ResumeAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Leaves the channel and drops the player.</summary>
    ValueTask DisconnectAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Arms the delayed leave. Calling it again re-arms rather than stacking.</summary>
    void ScheduleIdleDisconnect(ulong guildId, TimeSpan delay);

    /// <summary>Disarms it — somebody came back.</summary>
    void CancelIdleDisconnect(ulong guildId);
}

/// <summary>One voice-state change, flattened to what the decision needs.</summary>
/// <param name="IsBot">True when the account that moved is Sonarr itself — the drag case.</param>
public sealed record VoiceMove(
    ulong GuildId,
    ulong UserId,
    ulong? OldChannelId,
    ulong? NewChannelId,
    bool IsBot);

/// <summary>What the coordinator decided to do. Also the assertion surface for the tests.</summary>
public enum VoiceMoveAction
{
    /// <summary>Nothing to do — no player here, or the move does not concern us.</summary>
    None,

    /// <summary>The bot was dragged: the player was re-bound to the new channel.</summary>
    Reconnected,

    /// <summary>The bot was dragged but the gateway refused. Logged, swallowed, playback left alone.</summary>
    ReconnectFailed,

    /// <summary>The bot was disconnected outright — player dropped.</summary>
    Disconnected,

    /// <summary>Last human left: paused and the 300 s leave timer is armed.</summary>
    PausedEmpty,

    /// <summary>Somebody came back: resumed and the leave timer is disarmed.</summary>
    ResumedOccupied,
}

public sealed record VoiceMoveOutcome(VoiceMoveAction Action, ulong? ChannelId = null)
{
    public static VoiceMoveOutcome None { get; } = new(VoiceMoveAction.None);
}
