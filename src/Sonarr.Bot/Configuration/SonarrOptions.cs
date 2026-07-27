using System.ComponentModel.DataAnnotations;

namespace Sonarr.Bot.Configuration;

/// <summary>
/// Everything the process needs from the environment. Bound at boot and validated
/// eagerly (docs/11-deployment.md: "Validated at boot; boot fails loudly on bad config").
/// Names match .env.example exactly.
/// </summary>
public sealed class SonarrOptions
{
    public const string SectionName = "Sonarr";

    /// <summary>Bot token. Never logged.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "DISCORD_TOKEN is required.")]
    public string DiscordToken { get; init; } = "";

    /// <summary>
    /// When set, slash commands register to this guild only (instant updates, dev).
    /// Null/0 in prod = global registration. docs/07-commands.md#design-rules.
    /// </summary>
    public ulong? DiscordDevGuildId { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "LAVALINK_URI is required.")]
    public string LavalinkUri { get; init; } = "";

    [Required(AllowEmptyStrings = false, ErrorMessage = "LAVALINK_PASSWORD is required.")]
    public string LavalinkPassword { get; init; } = "";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PG_CONNECTION is required.")]
    public string PgConnection { get; init; } = "";

    [Required(AllowEmptyStrings = false, ErrorMessage = "REDIS_CONNECTION is required.")]
    public string RedisConnection { get; init; } = "";

    /// <summary>
    /// Admin-panel allow-list. Parsed from a comma-separated ADMIN_USER_IDS.
    /// Authority for admin routes is still Postgres per request (docs/09), this is the seed.
    /// </summary>
    public IReadOnlyList<ulong> AdminUserIds { get; init; } = [];

    /// <summary>Public base URL of the panels, used in DM login messages.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PANEL_BASE_URL is required.")]
    public string PanelBaseUrl { get; init; } = "";

    /// <summary>Where BackupRunner writes dumps (docs/11). Container path.</summary>
    public string BackupPath { get; init; } = "/backups";

    /// <summary>Persona directory, hot-reloaded (docs/10). Container path.</summary>
    public string PersonaPath { get; init; } = "/app/persona";

    /// <summary>
    /// Directory holding model.onnx + vocab.txt for the semantic tier (docs/03). Container path.
    /// Missing files are not fatal: matching degrades to lexical-only.
    /// </summary>
    public string ModelPath { get; init; } = "/app/models/minilm-l6-v2";

    /// <summary>Kestrel port for the panel API (docs/02: 5088, plain HTTP behind the tunnel).</summary>
    [Range(1, 65535)]
    public int ApiPort { get; init; } = 5088;
}
