namespace Sonarr.Elaine.Persona.Yaml;

// Mutable, nullable, forgiving DTOs: YamlDotNet fills these, then PersonaLoader validates
// and projects them into the immutable graph. Keeping the two apart is what lets a broken
// file produce a list of issues instead of an exception.

internal sealed class RawRoot
{
    public int Version { get; set; }
    public string? StartActivity { get; set; }
    public List<RawActivity>? Activities { get; set; }
    public RawPersonality? Personality { get; set; }
    public List<RawTier>? Tiers { get; set; }
    public List<RawMode>? Modes { get; set; }
    public List<string>? ModeCoveragePools { get; set; }
    public List<string>? HostPools { get; set; }
    public List<string>? Slots { get; set; }
}

internal sealed class RawPersonality
{
    public Dictionary<string, double>? Baselines { get; set; }
    public Dictionary<string, double>? Decay { get; set; }
}

internal sealed class RawActivity
{
    public string? Id { get; set; }
    public List<string>? Intents { get; set; }
    public string? FallbackPool { get; set; }
}

internal sealed class RawTier
{
    public string? Id { get; set; }
    public double MinTrust { get; set; }
}

internal sealed class RawMode
{
    public string? Id { get; set; }
    public List<string>? When { get; set; }
    public bool Always { get; set; }
}

internal sealed class RawIntentFile
{
    public List<RawIntent>? Intents { get; set; }
}

internal sealed class RawIntent
{
    public string? Id { get; set; }
    public RawMatch? Match { get; set; }
    public List<string>? Examples { get; set; }
    public List<RawGuard>? Guards { get; set; }
    public string? Pool { get; set; }
    public string? Template { get; set; }
    public string? Topic { get; set; }
    public Dictionary<string, string>? Learns { get; set; }
    public List<string>? Asks { get; set; }
    public string? Push { get; set; }
    public bool Pop { get; set; }
    public Dictionary<string, double>? Affect { get; set; }
    public bool SideEffect { get; set; }
    public bool Once { get; set; }
    public int Cooldown { get; set; }
    public double? Specificity { get; set; }
}

internal sealed class RawMatch
{
    public List<string>? Keyword { get; set; }
    public List<string>? AllKeywords { get; set; }
    public List<string>? Fuzzy { get; set; }
    public List<string>? Regex { get; set; }
    public List<string>? Style { get; set; }
    public int MaxDistance { get; set; } = 1;
    public int MinLength { get; set; } = 4;
}

internal sealed class RawGuard
{
    public string? Kind { get; set; }
    public string? Value { get; set; }
    public double Bonus { get; set; }
}

internal sealed class RawPoolFile
{
    // A pool is either a bare list of lines, or a map of mode -> lines (with `default`).
    public Dictionary<string, object?>? Pools { get; set; }
}

internal sealed class RawStanceFile
{
    public List<RawStance>? Stances { get; set; }
}

internal sealed class RawStance
{
    public string? Topic { get; set; }
    public string? Stance { get; set; }
    public string? Pool { get; set; }
    public double Strength { get; set; } = 1;
}

internal sealed class RawOverlayFile
{
    public string? Id { get; set; }
    public string? Activation { get; set; }
    public int Priority { get; set; }
    public Dictionary<string, string>? Replaces { get; set; }
}
