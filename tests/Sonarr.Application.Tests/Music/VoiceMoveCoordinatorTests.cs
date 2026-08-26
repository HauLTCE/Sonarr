using Sonarr.Application.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Application.Tests.Music;

/// <summary>
/// The voice-state rules, with the drag-to-another-channel regression first: the old bot crashed
/// when an admin moved it, so this is the test that must never go red
/// (docs/checklist.md — Music: "dragged to another VC → reconnect, don't crash").
/// </summary>
public sealed class VoiceMoveCoordinatorTests
{
    [Fact]
    public async Task Dragged_to_another_voice_channel_reconnects_and_keeps_playing()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator();

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.Reconnected, outcome.Action);
        Assert.Equal(Build.Stage, outcome.ChannelId);
        Assert.Equal([Build.Stage], gateway.Reconnects);

        // Still playing: nothing paused it, nothing disconnected it.
        Assert.Equal(0, gateway.Pauses);
        Assert.Equal(0, gateway.Disconnects);
        Assert.False(gateway.Paused);
    }

    [Fact]
    public async Task A_failing_reconnect_is_reported_not_thrown()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator();
        gateway.ReconnectThrows = true;

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.ReconnectFailed, outcome.Action);
    }

    [Fact]
    public async Task Dragged_into_an_empty_channel_pauses_and_arms_the_leave_timer()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(listeners: 0);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.PausedEmpty, outcome.Action);
        Assert.Equal([Build.Stage], gateway.Reconnects);
        Assert.Equal(1, gateway.Pauses);
        Assert.Equal(MusicRules.EmptyChannelDisconnectDelay, gateway.ScheduledDelay);
    }

    [Fact]
    public async Task Disconnected_outright_drops_the_player()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator();

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, null, IsBot: true));

        Assert.Equal(VoiceMoveAction.Disconnected, outcome.Action);
        Assert.Equal(1, gateway.Disconnects);
        Assert.Empty(gateway.Reconnects);
    }

    [Fact]
    public async Task A_move_with_no_player_does_nothing()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(playerChannel: null);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Empty(gateway.Reconnects);
    }

    [Fact]
    public async Task Mute_and_deafen_events_are_ignored()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator();

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Member, Build.Lobby, Build.Lobby, IsBot: false));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Equal(0, gateway.Pauses);
        Assert.Equal(0, gateway.Cancels);
    }

    [Fact]
    public async Task Last_listener_leaving_pauses_and_arms_the_leave_timer()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(listeners: 0);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Member, Build.Lobby, null, IsBot: false));

        Assert.Equal(VoiceMoveAction.PausedEmpty, outcome.Action);
        Assert.Equal(1, gateway.Pauses);
        Assert.Equal(MusicRules.EmptyChannelDisconnectDelay, gateway.ScheduledDelay);
    }

    [Fact]
    public async Task Somebody_coming_back_resumes_and_disarms_the_leave_timer()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(listeners: 1);
        gateway.Paused = true;

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Member, null, Build.Lobby, IsBot: false));

        Assert.Equal(VoiceMoveAction.ResumedOccupied, outcome.Action);
        Assert.Equal(1, gateway.Resumes);
        Assert.Equal(1, gateway.Cancels);
    }

    [Fact]
    public async Task A_move_between_channels_we_are_not_in_is_ignored()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(listeners: 0);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Member, 900UL, 901UL, IsBot: false));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Equal(0, gateway.Pauses);
    }

    /// <summary>
    /// The <c>/play</c> → kick → <c>/play</c> bug. Sonarr's own join arrives as a bot move with no
    /// old channel; treating it as a drag re-sent the voice update on a channel Lavalink had
    /// already handshaked (voice close 4006, silent audio) and then judged occupancy on a member
    /// cache that had not caught up, pausing the track that had just started.
    /// </summary>
    /// <remarks>
    /// <c>listeners: 0</c> on purpose: with the guard removed this reconnects AND pauses, so all
    /// three assertions below fail rather than only the first. A test that passes because the fake
    /// happened to report somebody present would not be testing the guard at all.
    /// </remarks>
    [Fact]
    public async Task Our_own_join_is_not_a_drag_and_touches_nothing()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(listeners: 0);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, null, Build.Lobby, IsBot: true));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Empty(gateway.Reconnects);
        Assert.Equal(0, gateway.Pauses);
        Assert.Null(gateway.ScheduledDelay);
    }

    /// <summary>
    /// A cache miss is not an empty channel. Pausing on a guess costs a silent track, and the
    /// gateway now says so with <see cref="IVoicePlayerGateway.UnknownListeners"/> instead of zero.
    /// </summary>
    /// <remarks>
    /// <c>Paused = true</c> is what makes this bite: without the unknown branch, −1 is neither zero
    /// nor a reason to keep quiet, so the coordinator falls through to the resume half and undoes a
    /// deliberate <c>/pause</c> — which the <c>Resumes</c> and <c>Cancels</c> assertions catch.
    /// </remarks>
    [Fact]
    public async Task Unknown_occupancy_decides_nothing()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) =
            Build.Coordinator(listeners: IVoicePlayerGateway.UnknownListeners);
        gateway.Paused = true;

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Member, null, Build.Lobby, IsBot: false));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Equal(0, gateway.Resumes);
        Assert.Equal(0, gateway.Pauses);
        Assert.Equal(0, gateway.Cancels);
        Assert.Null(gateway.ScheduledDelay);
        Assert.True(gateway.Paused);
    }

    /// <summary>
    /// Same rule on the drag path: reconnect, then leave playback exactly as it was.
    /// </summary>
    /// <remarks>
    /// <c>Paused = true</c> again, and for the same reason — an unknown count that falls through to
    /// the occupancy rules resumes a deliberately paused player and reports
    /// <c>ResumedOccupied</c> instead of <c>Reconnected</c>, so the first assertion catches it too.
    /// </remarks>
    [Fact]
    public async Task Dragged_into_a_channel_we_cannot_see_reconnects_but_leaves_playback_alone()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) =
            Build.Coordinator(listeners: IVoicePlayerGateway.UnknownListeners);
        gateway.Paused = true;

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.Reconnected, outcome.Action);
        Assert.Equal([Build.Stage], gateway.Reconnects);
        Assert.Equal(0, gateway.Pauses);
        Assert.Equal(0, gateway.Resumes);
        Assert.True(gateway.Paused);
        Assert.Null(gateway.ScheduledDelay);
    }

    /// <summary>
    /// A drag that finds no player disarms the timer on the way out — a leave that armed one and
    /// then lost its player would otherwise fire <c>DisconnectAsync</c> against nothing forever.
    /// </summary>
    [Fact]
    public async Task A_drag_with_no_player_disarms_the_leave_timer()
    {
        (VoiceMoveCoordinator coordinator, FakeVoiceGateway gateway) = Build.Coordinator(playerChannel: null);

        VoiceMoveOutcome outcome = await coordinator.HandleAsync(
            new VoiceMove(Build.Guild, Build.Bot, Build.Lobby, Build.Stage, IsBot: true));

        Assert.Equal(VoiceMoveAction.None, outcome.Action);
        Assert.Equal(1, gateway.Cancels);
        Assert.Equal(0, gateway.Disconnects);
    }
}
