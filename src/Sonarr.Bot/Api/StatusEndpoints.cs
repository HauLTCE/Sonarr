using System.Text.Json;
using System.Text.Json.Nodes;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Bot.Observability;

namespace Sonarr.Bot.Api;

/// <summary>
/// <c>GET /api/status</c> — the public status blob (docs/09). No auth, so nothing here may name a
/// user, a channel or a guild.
/// </summary>
/// <remarks>
/// The live half comes from <c>web:live_status</c>, written by the pusher at most once every 5 s,
/// so a status page left open cannot turn into a load generator on the gateway or Lavalink.
/// </remarks>
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapSonarrStatus(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/api/status", GetStatusAsync);

        return routes;
    }

    private static async Task<IResult> GetStatusAsync(
        ISelfTest selfTest, IWebSessionCache cache, SonarrMetrics metrics, CancellationToken ct)
    {
        SelfTestReport? report = selfTest.Last;
        string? liveJson = await cache.GetLiveStatusJsonAsync(ct);

        return Results.Ok(new
        {
            // "unknown" before the first probe run rather than a cheerful default: the page should
            // not claim green for a bot that has been up four seconds.
            status = report is null ? "unknown" : report.Healthy ? "ok" : "degraded",
            startedAt = metrics.StartedAt,
            uptimeSeconds = (long)(DateTimeOffset.UtcNow - metrics.StartedAt).TotalSeconds,
            checkedAt = report?.RanAt,
            checks = report?.Checks.Select(c => new { c.Name, c.Healthy, c.Detail }),

            // Passed through as a node, not a string, so the page gets one object to read. Null
            // when the blob is older than 5 s — a stale latency number is worse than none.
            live = Parse(liveJson),
        });
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // Cache contents are ours, but a truncated value must not 500 the status page.
            return null;
        }
    }
}
