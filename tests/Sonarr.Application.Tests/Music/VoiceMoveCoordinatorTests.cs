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
}
