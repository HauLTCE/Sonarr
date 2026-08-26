using Microsoft.Extensions.Logging;

namespace Sonarr.Application.Music;

/// <summary>
/// The voice-state decision for music: <b>the VC-drag fix</b> plus auto-pause on an empty channel
/// (docs/08-background-services.md — VoiceStateWatcher).
/// </summary>
/// <remarks>
/// <para>
/// It lives here, not in the gateway handler, for one reason: an admin dragging the bot used to
/// crash the old bot, and a crash-regression needs a test that can run without Discord. The
/// handler in Sonarr.Bot translates the event and does nothing else.
/// </para>
/// <para>
/// <b>Never throws.</b> A voice-state handler that throws takes the gateway callback with it, so
/// every gateway call is wrapped: a failed reconnect is a log line and
/// <see cref="VoiceMoveAction.ReconnectFailed"/>, not an exception.
/// </para>
/// <para>
/// <b>A join is not a move.</b> Sonarr's own join arrives here as a bot move with no old channel,
/// and acting on it — re-sending the voice update, or judging occupancy — is what made <c>/play</c>
/// silent until you kicked her and ran it again. See <c>HandleBotMoveAsync</c> for the mechanism.
/// </para>
/// </remarks>
public sealed class VoiceMoveCoordinator(IVoicePlayerGateway gateway, ILogger<VoiceMoveCoordinator> logger)
{
    public async Task<VoiceMoveOutcome> HandleAsync(VoiceMove move, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(move);

        if (move.OldChannelId == move.NewChannelId)
        {
            // Mute/deafen/stream flips arrive on the same event. Not our business.
            return VoiceMoveOutcome.None;
        }

        try
        {
            return move.IsBot
                ? await HandleBotMoveAsync(move, cancellationToken).ConfigureAwait(false)
                : await HandleMemberMoveAsync(move, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The old bot died here. Now it is a log line.
            logger.LogWarning(
                ex,
                "Voice state change for guild {GuildId} could not be handled ({Old} -> {New}).",
                move.GuildId,
                move.OldChannelId,
                move.NewChannelId);
            return VoiceMoveOutcome.None;
        }
    }

    /// <summary>Sonarr itself moved: dragged to another channel, or disconnected.</summary>
    private async Task<VoiceMoveOutcome> HandleBotMoveAsync(VoiceMove move, CancellationToken ct)
    {
        if (move.OldChannelId is null)
        {
            // We just joined. This is not a drag, and treating it as one is what broke /play.
            //
            // Every join emits a bot voice-state event with OldChannelId null, and this method used
            // to fall straight through to ReconnectAsync — a second op4 for a channel Lavalink4NET
            // had already completed the handshake on. The node answers the new session id and token
            // with a voice websocket close 4006 (session no longer valid) and stops sending audio,
            // while the player object stays happily "playing". Then ApplyOccupancyAsync ran on the
            // same event and paused the brand-new player if the member cache had not caught up yet.
            //
            // That is the /play → kick → /play dance: the kick disposed the wrecked player, and the
            // second /play got a clean one because by then the member cache was warm and the stale
            // session was gone. Nothing to rescue on a join, so nothing to do.
            return VoiceMoveOutcome.None;
        }

        if (await gateway.GetPlayerChannelAsync(move.GuildId, ct).ConfigureAwait(false) is null)
        {
            // No player: we are not playing anything, so there is nothing to rescue. The timer is
            // still disarmed, because a leave that armed one and then lost its player would keep
            // firing DisconnectAsync against nothing every five minutes.
            gateway.CancelIdleDisconnect(move.GuildId);
            return VoiceMoveOutcome.None;
        }

        if (move.NewChannelId is not { } destination)
        {
            // Yanked out entirely. Drop the player rather than leave it pointing at nothing.
            gateway.CancelIdleDisconnect(move.GuildId);
            await gateway.DisconnectAsync(move.GuildId, ct).ConfigureAwait(false);
            return new VoiceMoveOutcome(VoiceMoveAction.Disconnected);
        }

        try
        {
            await gateway.ReconnectAsync(move.GuildId, destination, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Reconnect after being moved to {ChannelId} failed in guild {GuildId}.",
                destination, move.GuildId);
            return new VoiceMoveOutcome(VoiceMoveAction.ReconnectFailed, destination);
        }

        logger.LogInformation(
            "Moved to voice channel {ChannelId} in guild {GuildId}; player reconnected.",
            destination, move.GuildId);

        // The new room may already be empty — same rule as anyone else leaving.
        VoiceMoveOutcome occupancy = await ApplyOccupancyAsync(move.GuildId, destination, ct).ConfigureAwait(false);
        return occupancy.Action is VoiceMoveAction.None
            ? new VoiceMoveOutcome(VoiceMoveAction.Reconnected, destination)
            : occupancy;
    }

    /// <summary>A member moved: only matters when it changes our channel's occupancy.</summary>
    private async Task<VoiceMoveOutcome> HandleMemberMoveAsync(VoiceMove move, CancellationToken ct)
    {
        if (await gateway.GetPlayerChannelAsync(move.GuildId, ct).ConfigureAwait(false) is not { } playerChannel)
        {
            return VoiceMoveOutcome.None;
        }

        if (move.OldChannelId != playerChannel && move.NewChannelId != playerChannel)
        {
            return VoiceMoveOutcome.None;
        }

        return await ApplyOccupancyAsync(move.GuildId, playerChannel, ct).ConfigureAwait(false);
    }

    /// <summary>Empty → pause and arm the 300 s leave. Occupied again → resume and disarm.</summary>
    private async Task<VoiceMoveOutcome> ApplyOccupancyAsync(ulong guildId, ulong channelId, CancellationToken ct)
    {
        var listeners = await gateway.CountListenersAsync(guildId, channelId, ct).ConfigureAwait(false);

        if (listeners == IVoicePlayerGateway.UnknownListeners)
        {
            // Occupancy is a guess and pausing on a guess costs a silent track, so an unknown
            // count decides nothing at all — not even the resume half, because "somebody is
            // probably there" is not a reason to undo a deliberate /pause.
            logger.LogDebug(
                "Occupancy unknown for {ChannelId} in guild {GuildId}; leaving playback alone.",
                channelId, guildId);
            return VoiceMoveOutcome.None;
        }

        var paused = await gateway.IsPausedAsync(guildId, ct).ConfigureAwait(false);

        if (listeners == 0)
        {
            if (!paused)
            {
                await gateway.PauseAsync(guildId, ct).ConfigureAwait(false);
            }

            gateway.ScheduleIdleDisconnect(guildId, Domain.Music.MusicRules.EmptyChannelDisconnectDelay);
            return new VoiceMoveOutcome(VoiceMoveAction.PausedEmpty, channelId);
        }

        gateway.CancelIdleDisconnect(guildId);

        if (!paused)
        {
            return VoiceMoveOutcome.None;
        }

        // Only auto-resume what auto-pause stopped… we cannot tell the difference here, and
        // "music comes back when people do" is the behaviour people expect.
        // ponytail: if someone complains about /pause being undone by a rejoin, track the
        // pause origin in the player state and only resume our own.
        await gateway.ResumeAsync(guildId, ct).ConfigureAwait(false);
        return new VoiceMoveOutcome(VoiceMoveAction.ResumedOccupied, channelId);
    }
}
