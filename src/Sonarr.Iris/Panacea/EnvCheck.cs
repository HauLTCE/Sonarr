namespace Sonarr.Iris.Panacea;

/// <summary>
/// The six keys the bot boots on, checked the way <c>SonarrOptionsSetup.Validate</c> checks
/// them: presence first, then the shapes annotations cannot express. Runs before everything
/// else because every other check reads what these keys name — and there is no remedy, since
/// a secret only a person can supply is not something a doctor manufactures. The report says
/// exactly which keys are missing and where they belong, which is the fix.
/// </summary>
internal sealed class EnvCheck : IDoctorCheck
{
    /// <summary>The keys <c>SonarrOptionsSetup.Bind</c> reads, minus the three with defaults.</summary>
    private static readonly string[] RequiredKeys =
    [
        "DISCORD_TOKEN",
        "LAVALINK_URI",
        "LAVALINK_PASSWORD",
        "PG_CONNECTION",
        "REDIS_CONNECTION",
        "ADMIN_USER_IDS",
    ];

    public string Name => "env";

    public Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        List<string> missing = [.. RequiredKeys.Where(IsBlank)];
        if (missing.Count > 0)
        {
            return Task.FromResult(Diagnosis.Broken(
                $"{Output.Count(missing.Count, "key", "keys")} not set: {string.Join(", ", missing)}",
                "set them in the .env next to the compose file (or point SONARR_ENV at it); "
                + "the bot refuses to boot without them"));
        }

        List<string> shape = ShapeProblems();
        return Task.FromResult(shape.Count == 0
            ? Diagnosis.Ok($"all {RequiredKeys.Length} keys set")
            : Diagnosis.Broken(
                string.Join("; ", shape),
                "fix the values in the .env — key names above, values never leave the box"));
    }

    private List<string> ShapeProblems()
    {
        List<string> problems = [];

        string token = Value("DISCORD_TOKEN");
        if (token.Length is > 0 and < 50)
        {
            problems.Add("DISCORD_TOKEN looks too short to be a bot token");
        }

        string uri = Value("LAVALINK_URI");
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? lavalink))
        {
            problems.Add("LAVALINK_URI is not an absolute URI");
        }
        else if (lavalink.Scheme is not ("http" or "https"))
        {
            problems.Add($"LAVALINK_URI scheme '{lavalink.Scheme}' is not http/https");
        }

        string admins = Value("ADMIN_USER_IDS");
        if (admins.Length == 0)
        {
            problems.Add("ADMIN_USER_IDS is empty — nobody could change global config");
        }
        else if (admins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(id => !ulong.TryParse(id, out _)))
        {
            problems.Add("ADMIN_USER_IDS holds something that is not a Discord id");
        }

        return problems;
    }

    private static bool IsBlank(string key) => CliHost.Configuration[key] is not { Length: > 0 } v
        || string.IsNullOrWhiteSpace(v);

    private static string Value(string key) => CliHost.Configuration[key]?.Trim() ?? "";
}
