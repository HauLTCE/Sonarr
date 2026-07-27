using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Sonarr.Bot.Configuration;

/// <summary>
/// Binds <see cref="SonarrOptions"/> from flat environment variables (.env / compose
/// env_file) and validates it before anything else starts. Boot fails loudly on bad
/// config rather than dying later on the first Discord call (docs/11-deployment.md).
/// </summary>
public static class SonarrOptionsSetup
{
    public static SonarrOptions AddSonarrOptions(this IServiceCollection services, IConfiguration config)
    {
        var options = Bind(config);
        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new OptionsValidationException(
                SonarrOptions.SectionName, typeof(SonarrOptions),
                ["Configuration is invalid — fix .env and restart:", .. errors.Select(e => "  • " + e)]);
        }

        services.AddSingleton(options);
        return options;
    }

    private static SonarrOptions Bind(IConfiguration c) => new()
    {
        DiscordToken = Str(c, "DISCORD_TOKEN"),
        DiscordDevGuildId = OptionalUlong(c, "DISCORD_DEV_GUILD_ID"),
        LavalinkUri = Str(c, "LAVALINK_URI"),
        LavalinkPassword = Str(c, "LAVALINK_PASSWORD"),
        PgConnection = Str(c, "PG_CONNECTION"),
        RedisConnection = Str(c, "REDIS_CONNECTION"),
        AdminUserIds = UlongList(c, "ADMIN_USER_IDS"),
        PanelBaseUrl = Str(c, "PANEL_BASE_URL"),
        BackupPath = Str(c, "BACKUP_PATH", "/backups"),
        PersonaPath = Str(c, "PERSONA_PATH", "/app/persona"),
        ModelPath = Str(c, "MODEL_PATH", "/app/models/minilm-l6-v2"),
        ApiPort = OptionalUlong(c, "API_PORT") is { } p ? (int)p : 5088,
    };

    /// <summary>
    /// Data-annotation errors plus the cross-field checks annotations cannot express.
    /// Never echo secret values into the message — key names only.
    /// </summary>
    private static List<string> Validate(SonarrOptions o)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(o, new ValidationContext(o), results, validateAllProperties: true);
        var errors = results.Select(r => r.ErrorMessage ?? "invalid value").ToList();

        if (!Uri.TryCreate(o.LavalinkUri, UriKind.Absolute, out var lavalink))
        {
            errors.Add("LAVALINK_URI must be an absolute URI (e.g. http://127.0.0.1:2333).");
        }
        else if (lavalink.Scheme is not ("http" or "https"))
        {
            errors.Add($"LAVALINK_URI scheme '{lavalink.Scheme}' is not http/https.");
        }

        if (!Uri.TryCreate(o.PanelBaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("PANEL_BASE_URL must be an absolute URL (it goes into login DMs).");
        }

        // A token-shaped sanity check only — we cannot verify it without calling Discord,
        // but "you pasted the client secret" is a common and very confusing failure.
        if (o.DiscordToken.Length is > 0 and < 50)
        {
            errors.Add("DISCORD_TOKEN looks too short to be a bot token.");
        }

        if (o.AdminUserIds.Count == 0)
        {
            errors.Add("ADMIN_USER_IDS is empty — nobody could reach the admin panel.");
        }

        return errors;
    }

    private static string Str(IConfiguration c, string key, string? fallback = null)
        => c[key] is { Length: > 0 } v ? v.Trim() : fallback ?? "";

    private static ulong? OptionalUlong(IConfiguration c, string key)
        => ulong.TryParse(c[key], CultureInfo.InvariantCulture, out var v) && v != 0 ? v : null;

    private static IReadOnlyList<ulong> UlongList(IConfiguration c, string key)
        => (c[key] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => ulong.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : 0UL)
            .Where(v => v != 0)
            .Distinct()
            .ToArray();
}
