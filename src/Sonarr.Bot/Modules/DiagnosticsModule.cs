using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Bot.Modules;

/// <summary>
/// The two commands that must work even when everything else is off
/// (docs/checklist.md — "Kill switches &amp; health").
/// </summary>
// Deliberately NOT SonarrModuleBase: that carries the fail-closed flood guard, which would
// silence the two commands you need during exactly the outage they report on.
public sealed class DiagnosticsModule(DiscordSocketClient client, ISelfTest selfTest)
    : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    [SlashCommand("ping", "Check that I'm awake and how slow the link is.")]
    public Task PingAsync()
        => RespondAsync($"Still here. Gateway latency {client.Latency} ms.");

    [SlashCommand("status", "Uptime and connection health.")]
    public Task StatusAsync()
    {
        var uptime = DateTimeOffset.UtcNow - StartedAt;

        // The self-test runs on boot and hourly, so this is at most an hour stale — say so
        // rather than probing the dependencies again on every /status.
        var dependencies = selfTest.Last is { } report
            ? $"Dependencies: {report.Summary} (checked {Ago(report.RanAt)})"
            : "Dependencies: first self-test hasn't finished yet.";

        return RespondAsync(
            $"""
            **Sonarr**
            Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m
            Gateway: {client.ConnectionState} ({client.Latency} ms)
            Guilds: {client.Guilds.Count}
            {dependencies}
            """);
    }

    private static string Ago(DateTimeOffset when)
    {
        var since = DateTimeOffset.UtcNow - when;
        return since < TimeSpan.FromMinutes(1)
            ? "just now"
            : $"{(int)since.TotalMinutes} min ago";
    }
}
