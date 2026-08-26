using System.Globalization;

namespace Sonarr.Domain.Configuration;

/// <summary>
/// A stored config value plus the key definition it was validated against. Values are
/// canonicalised on write, so the accessors below are the typed view of the same string.
/// </summary>
public sealed record ConfigValue(ConfigKeyDefinition Definition, string Raw)
{
    /// <summary>Channel/role id, or <c>null</c> if the stored value is not a snowflake.</summary>
    public ulong? AsSnowflake => ulong.TryParse(Raw, out var id) ? id : null;

    public bool? AsBoolean => Raw switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    public int? AsInteger =>
        int.TryParse(Raw, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>Outcome of a single <c>/config set</c>. <paramref name="Message"/> is user-facing.</summary>
public sealed record ConfigWriteResult(bool Success, string Message)
{
    public static ConfigWriteResult Ok(string message) => new(true, message);

    public static ConfigWriteResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Outcome of an import. Nothing is written unless <see cref="Applied"/> is true —
/// a rejected import leaves the guild exactly as it was.
/// </summary>
public sealed record ConfigImportResult(bool Applied, int KeyCount, IReadOnlyList<string> Rejections)
{
    public static ConfigImportResult Ok(int keyCount) => new(true, keyCount, []);

    public static ConfigImportResult Rejected(IReadOnlyList<string> rejections) => new(false, 0, rejections);
}

/// <summary>Where a kill-switch state came from — shown by <c>/feature list</c>.</summary>
public enum FeatureStateSource
{
    /// <summary>
    /// No row anywhere: a module is on and a restriction is off
    /// (<see cref="FeatureNames.DefaultState"/>).
    /// </summary>
    Default,

    /// <summary>The global (guild_id = 0) row.</summary>
    Global,

    /// <summary>This guild's row, which overrides the global one.</summary>
    Guild,
}

public sealed record FeatureState(string Feature, bool Enabled, FeatureStateSource Source);
