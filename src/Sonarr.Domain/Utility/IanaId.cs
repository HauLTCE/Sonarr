namespace Sonarr.Domain.Utility;

/// <summary>
/// IANA timezone id handling that does not need ICU. <c>InvariantGlobalization=true</c>
/// (Directory.Build.props) removes ICU, so <see cref="TimeZoneInfo"/> resolves ids from the OS tz
/// database only: real lookups work in the Linux container (tzdata is installed) and fail on a
/// Windows dev box.
/// </summary>
/// <remarks>
/// // ponytail: shape validation plus a curated suggestion list, not the full 600-id tzdata
/// catalog. Ceiling: a well-formed id for a city that doesn't exist is accepted on a machine
/// without tzdata, and autocomplete only offers the ~50 zones this community plausibly uses.
/// Upgrade path: reference TimeZoneConverter (bundles the whole mapping) if either bites.
/// </remarks>
public static class IanaId
{
    /// <summary>The IANA area prefixes — the part of an id that is a genuinely closed set.</summary>
    private static readonly string[] Areas =
    [
        "Africa", "America", "Antarctica", "Arctic", "Asia", "Atlantic",
        "Australia", "Europe", "Indian", "Pacific", "Etc",
    ];

    /// <summary>
    /// True when <paramref name="id"/> is a plausible IANA id: a real area, and segments made of
    /// the characters tzdata actually uses. Does not prove the zone exists.
    /// </summary>
    public static bool LooksValid(string? id)
    {
        var raw = id?.Trim() ?? string.Empty;
        if (raw is "UTC")
        {
            return true;
        }

        var parts = raw.Split('/');
        return parts.Length is 2 or 3
               && Areas.Contains(parts[0])
               && parts.All(p => p.Length > 0
                                 && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+'));
    }

    /// <summary>
    /// Autocomplete source for <c>/timezone</c>: the zones this community plausibly uses, hard
    /// coded so the picker works identically on a dev box and in the container.
    /// </summary>
    public static IReadOnlyList<string> Suggestions { get; } =
    [
        "UTC",
        "Asia/Ho_Chi_Minh", "Asia/Bangkok", "Asia/Singapore", "Asia/Jakarta", "Asia/Manila",
        "Asia/Hong_Kong", "Asia/Shanghai", "Asia/Taipei", "Asia/Tokyo", "Asia/Seoul",
        "Asia/Kolkata", "Asia/Karachi", "Asia/Dubai", "Asia/Tehran", "Asia/Jerusalem",
        "Asia/Kathmandu", "Asia/Colombo", "Asia/Almaty", "Asia/Yekaterinburg",
        "Europe/London", "Europe/Dublin", "Europe/Lisbon", "Europe/Madrid", "Europe/Paris",
        "Europe/Brussels", "Europe/Amsterdam", "Europe/Berlin", "Europe/Zurich", "Europe/Rome",
        "Europe/Vienna", "Europe/Prague", "Europe/Warsaw", "Europe/Stockholm", "Europe/Oslo",
        "Europe/Copenhagen", "Europe/Helsinki", "Europe/Athens", "Europe/Bucharest",
        "Europe/Kyiv", "Europe/Istanbul", "Europe/Moscow",
        "America/New_York", "America/Toronto", "America/Chicago", "America/Denver",
        "America/Phoenix", "America/Los_Angeles", "America/Vancouver", "America/Anchorage",
        "America/Mexico_City", "America/Bogota", "America/Lima", "America/Santiago",
        "America/Sao_Paulo", "America/Buenos_Aires", "America/Halifax",
        "Africa/Cairo", "Africa/Lagos", "Africa/Nairobi", "Africa/Johannesburg", "Africa/Casablanca",
        "Australia/Perth", "Australia/Adelaide", "Australia/Brisbane", "Australia/Sydney",
        "Australia/Melbourne", "Pacific/Auckland", "Pacific/Fiji", "Pacific/Honolulu",
        "Atlantic/Reykjavik", "Indian/Maldives",
    ];

    /// <summary>
    /// Up to <paramref name="limit"/> suggestions matching <paramref name="typed"/>. A well-shaped
    /// id that isn't in the list is offered back verbatim, so an unlisted zone is still settable.
    /// </summary>
    public static IReadOnlyList<string> Match(string? typed, int limit = 25)
    {
        var text = typed?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return [.. Suggestions.Take(limit)];
        }

        List<string> hits =
        [
            .. Suggestions
                .Where(z => z.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(limit),
        ];

        if (hits.Count == 0 && LooksValid(text))
        {
            hits.Add(text);
        }

        return hits;
    }
}
