namespace Sonarr.Domain.Configuration;

/// <summary>What a config value is allowed to be — drives validation and the error text.</summary>
public enum ConfigValueKind
{
    /// <summary>Discord channel snowflake; accepts a mention or a raw id.</summary>
    ChannelId,

    /// <summary>Discord role snowflake; accepts a mention or a raw id.</summary>
    RoleId,

    /// <summary>IANA timezone id (<c>Asia/Ho_Chi_Minh</c>).</summary>
    Timezone,

    Boolean,

    Integer,

    /// <summary>
    /// Per-channel XP weight as <c>channelId:percent</c> pairs, comma separated
    /// (<c>123:150,456:0</c>) — see <see cref="Levels.LevelsConfigKeys.ParseWeights"/>.
    /// </summary>
    ChannelWeights,
}

/// <summary>One entry in the closed config-key set.</summary>
/// <param name="Key">Storage key in <c>core.guild_config</c>.</param>
/// <param name="Kind">Value shape.</param>
/// <param name="Description">Shown in autocomplete and <c>/config list</c>.</param>
/// <param name="Minimum">
/// Inclusive lower bound for <see cref="ConfigValueKind.Integer"/>, and for
/// <see cref="ConfigValueKind.ChannelWeights"/> the bound on one pair's percent — the value is a
/// list, so there is no whole-value bound to mean anything else. Both are read by the web panel to
/// set its number input's range, which is why the weights entry carries them at all.
/// </param>
/// <param name="Maximum">Inclusive upper bound; see <paramref name="Minimum"/>.</param>
public sealed record ConfigKeyDefinition(
    string Key,
    ConfigValueKind Kind,
    string Description,
    int Minimum = int.MinValue,
    int Maximum = int.MaxValue);

/// <summary>
/// The config key catalog (docs/04-database.md#coreguild_config). Closed set: anything not
/// listed here is rejected before it reaches Postgres, so a typo can never become a row.
/// </summary>
public static class ConfigKeys
{
    public const string WelcomeChannel = "welcome_channel";
    public const string LogChannel = "log_channel";
    public const string MusicChannel = "music_channel";
    public const string LevelupChannel = "levelup_channel";
    public const string AnnounceChannel = "announce_channel";
    public const string AutoroleId = "autorole_id";
    public const string DjRole = "dj_role";
    public const string Timezone = "timezone";
    public const string XpMultiplier = "xp_multiplier";
    public const string LevelupDm = "levelup_dm";

    public static IReadOnlyList<ConfigKeyDefinition> All { get; } =
    [
        new(WelcomeChannel, ConfigValueKind.ChannelId, "Where join/leave messages go"),
        new(LogChannel, ConfigValueKind.ChannelId, "Where moderation cases are logged"),
        new(MusicChannel, ConfigValueKind.ChannelId, "Channel music commands are limited to"),
        new(LevelupChannel, ConfigValueKind.ChannelId, "Where level-up announcements go"),
        new(AnnounceChannel, ConfigValueKind.ChannelId, "Default channel for /announce"),
        new(AutoroleId, ConfigValueKind.RoleId, "Role handed to every new member"),
        new(DjRole, ConfigValueKind.RoleId, "Role that bypasses music vote gates"),
        new(Timezone, ConfigValueKind.Timezone, "Server timezone, IANA id (Asia/Ho_Chi_Minh)"),
        new(XpMultiplier, ConfigValueKind.Integer, "XP event multiplier, 1-5", Minimum: 1, Maximum: 5),
        new(LevelupDm, ConfigValueKind.Boolean, "DM level-ups instead of posting them"),
        // Key names come from LevelsConfigKeys so the reader and the catalog can't drift.
        new(Levels.LevelsConfigKeys.XpDecay, ConfigValueKind.Boolean, "Inactive members lose XP over time"),
        new(Levels.LevelsConfigKeys.XpChannelWeights, ConfigValueKind.ChannelWeights,
            "Per-channel XP weight, channelId:percent pairs (123:150,456:0)",
            Minimum: Levels.LevelsConfigKeys.MinWeightPercent,
            Maximum: Levels.LevelsConfigKeys.MaxWeightPercent),
    ];

    public static bool TryGet(
        string? key,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ConfigKeyDefinition? definition)
    {
        var normalized = key?.Trim().ToLowerInvariant();
        definition = All.FirstOrDefault(d => d.Key == normalized);
        return definition is not null;
    }

    /// <summary>
    /// Validates a raw user value for <paramref name="key"/>. On success
    /// <paramref name="canonicalValue"/> is the normalized string that gets stored; on failure
    /// <paramref name="error"/> is a sentence that can be shown to the user as-is.
    /// </summary>
    public static bool TryValidate(
        string? key,
        string? rawValue,
        out string canonicalValue,
        out string error)
    {
        canonicalValue = string.Empty;

        if (!TryGet(key, out ConfigKeyDefinition? definition))
        {
            error = $"`{key}` isn't a config key I know.";
            return false;
        }

        var raw = rawValue?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            error = $"`{definition.Key}` needs a value.";
            return false;
        }

        error = string.Empty;
        switch (definition.Kind)
        {
            case ConfigValueKind.ChannelId:
                if (TryParseSnowflake(raw, out canonicalValue))
                {
                    return true;
                }

                error = $"`{definition.Key}` needs a channel — mention it (#general) or paste its id.";
                return false;

            case ConfigValueKind.RoleId:
                if (TryParseSnowflake(raw, out canonicalValue))
                {
                    return true;
                }

                error = $"`{definition.Key}` needs a role — mention it (@DJ) or paste its id.";
                return false;

            case ConfigValueKind.Timezone:
                if (IsKnownTimezone(raw))
                {
                    canonicalValue = raw;
                    return true;
                }

                error = $"`{raw}` isn't a timezone I recognise. Use an IANA id like `Asia/Ho_Chi_Minh`.";
                return false;

            case ConfigValueKind.Boolean:
                if (TryParseBoolean(raw, out var flag))
                {
                    canonicalValue = flag ? "true" : "false";
                    return true;
                }

                error = $"`{definition.Key}` is on or off — try `true` or `false`.";
                return false;

            case ConfigValueKind.Integer:
                if (int.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    && number >= definition.Minimum
                    && number <= definition.Maximum)
                {
                    canonicalValue = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"`{definition.Key}` needs a whole number between {definition.Minimum} and {definition.Maximum}.";
                return false;

            case ConfigValueKind.ChannelWeights:
                // Re-serialized from the parse so storage is normalized and the reader (which
                // silently skips junk pairs) can never disagree with what we accepted here.
                IReadOnlyDictionary<ulong, int> weights = Levels.LevelsConfigKeys.ParseWeights(raw);
                if (weights.Count > 0 && weights.Count == CountPairs(raw))
                {
                    canonicalValue = string.Join(',', weights.OrderBy(w => w.Key)
                        .Select(w => $"{w.Key}:{w.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
                    return true;
                }

                error = $"`{definition.Key}` takes `channelId:percent` pairs, {definition.Minimum}-{definition.Maximum}, comma separated — like `123:150,456:0`.";
                return false;

            default:
                error = $"`{definition.Key}` can't be set yet.";
                return false;
        }
    }

    /// <summary>
    /// How many pairs the user wrote — compared against how many parsed, so a typo is an error
    /// here instead of a silently dropped channel. A repeated channel counts once, matching the
    /// parse's last-wins behaviour.
    /// </summary>
    private static int CountPairs(string raw)
        => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(':', 2)[0])
            .Distinct()
            .Count();

    private static bool TryParseSnowflake(string raw, out string canonical)
    {
        // Accepts 123, <#123>, <@&123> — Discord sends mentions, humans paste ids.
        var digits = raw.Trim('<', '>', '#', '@', '&', '!');
        canonical = string.Empty;
        if (!ulong.TryParse(digits, out var id) || id == 0)
        {
            return false;
        }

        canonical = digits;
        return true;
    }

    private static bool TryParseBoolean(string raw, out bool value)
    {
        value = false;
        switch (raw.ToLowerInvariant())
        {
            case "true" or "on" or "yes" or "enabled" or "1":
                value = true;
                return true;
            case "false" or "off" or "no" or "disabled" or "0":
                return true;
            default:
                return false;
        }
    }

    /// <summary>The IANA area prefixes — the part of an id that is a closed set.</summary>
    private static readonly string[] IanaAreas =
    [
        "Africa", "America", "Antarctica", "Arctic", "Asia", "Atlantic",
        "Australia", "Europe", "Indian", "Pacific", "Etc",
    ];

    // ponytail: area prefix + shape, plus the runtime's own lookup where it works. Prod (Linux)
    // resolves IANA ids from tzdata; a Windows dev box with InvariantGlobalization cannot map
    // them at all, so there we accept a well-shaped id in a real area. Net effect: nonsense like
    // "Mars/Olympus" is rejected everywhere, a typo'd city slips through on Windows only.
    // Upgrade path: TimeZoneConverter (bundles the full mapping) if that ever bites.
    private static readonly bool RuntimeKnowsIanaIds = TimeZoneInfo.TryFindSystemTimeZoneById("Etc/UTC", out _);

    private static bool IsKnownTimezone(string raw)
    {
        if (raw is "UTC")
        {
            return true;
        }

        var parts = raw.Split('/');
        if (parts.Length is not (2 or 3)
            || !IanaAreas.Contains(parts[0])
            || !parts.All(p => p.Length > 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+')))
        {
            return false;
        }

        return !RuntimeKnowsIanaIds || TimeZoneInfo.TryFindSystemTimeZoneById(raw, out _);
    }
}
