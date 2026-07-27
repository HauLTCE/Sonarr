using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Music;
using Sonarr.Domain.Music;

namespace Sonarr.Application.Tests.Music;

/// <summary>
/// The voice gateway, in memory. Records what the coordinator asked for so a test can assert on
/// "did it reconnect" without a live Discord connection.
/// </summary>
internal sealed class FakeVoiceGateway : IVoicePlayerGateway
{
    public ulong? PlayerChannel { get; set; }

    public int Listeners { get; set; } = 1;

    public bool Paused { get; set; }

    public bool ReconnectThrows { get; set; }

    public List<ulong> Reconnects { get; } = [];

    public int Pauses { get; private set; }

    public int Resumes { get; private set; }

    public int Disconnects { get; private set; }

    public TimeSpan? ScheduledDelay { get; private set; }

    public int Cancels { get; private set; }

    public ValueTask<ulong?> GetPlayerChannelAsync(ulong guildId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(PlayerChannel);

    public ValueTask ReconnectAsync(ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default)
    {
        if (ReconnectThrows)
        {
            throw new InvalidOperationException("gateway said no");
        }

        Reconnects.Add(voiceChannelId);
        PlayerChannel = voiceChannelId;
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> CountListenersAsync(
        ulong guildId, ulong voiceChannelId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Listeners);

    public ValueTask<bool> IsPausedAsync(ulong guildId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Paused);

    public ValueTask PauseAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        Pauses++;
        Paused = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        Resumes++;
        Paused = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisconnectAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        Disconnects++;
        PlayerChannel = null;
        return ValueTask.CompletedTask;
    }

    public void ScheduleIdleDisconnect(ulong guildId, TimeSpan delay) => ScheduledDelay = delay;

    public void CancelIdleDisconnect(ulong guildId) => Cancels++;
}

/// <summary>Test data with names instead of magic numbers.</summary>
internal static class Build
{
    public const ulong Guild = 1000UL;
    public const ulong Bot = 42UL;
    public const ulong Member = 7UL;
    public const ulong Lobby = 200UL;
    public const ulong Stage = 300UL;

    public static (VoiceMoveCoordinator Coordinator, FakeVoiceGateway Gateway) Coordinator(
        ulong? playerChannel = Lobby, int listeners = 2)
    {
        FakeVoiceGateway gateway = new() { PlayerChannel = playerChannel, Listeners = listeners };
        return (new VoiceMoveCoordinator(gateway, NullLogger<VoiceMoveCoordinator>.Instance), gateway);
    }

    public static TrackInfo Track(string title, ulong requester, string? uri = null, long ms = 180_000)
        => new(title, "Artist", uri ?? $"https://example.test/{title}", title, ms, requester);
}
