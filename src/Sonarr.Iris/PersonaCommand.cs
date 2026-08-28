using Sonarr.Elaine.Persona;

using Sonarr.Iris.Persona;

namespace Sonarr.Iris;

/// <summary>
/// <c>sonarr persona</c> — load the persona exactly the way the bot loads it (same
/// <see cref="PersonaLoader"/>, same <see cref="DirectoryPersonaSource"/>) and report what the
/// validator sees: counts, warnings, and errors.
/// </summary>
/// <remarks>
/// The point is catching a bad edit before a restart: the bot refuses to boot on errors and keeps
/// the old graph on a hot reload, so this command answers "would a restart accept what is on
/// disk?" without a restart. Warnings print but pass — the same contract as boot.
/// </remarks>
internal static class PersonaCommand
{
    public static Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        (string root, PersonaValidationResult result) = Load(cli.Value("dir"));
        Output.Heading($"Persona — {root}");

        if (result.Graph is { } graph)
        {
            Output.Rows(
            [
                ("version", graph.Root.Version.ToString()),
                ("start", graph.Root.StartActivity),
                ("activities", graph.Root.Activities.Count.ToString()),
                ("intents", graph.Intents.Count.ToString()),
                ("pools", graph.Pools.Count.ToString()),
                ("stances", graph.Stances.Count.ToString()),
                ("overlays", graph.Overlays.Count.ToString()),
                ("modes", graph.Root.Modes.Count.ToString()),
                ("tiers", graph.Root.Tiers.Count.ToString()),
                ("slots", graph.Root.Slots.Count.ToString()),
            ]);
        }

        foreach (PersonaIssue issue in result.Warnings)
        {
            Console.WriteLine($"  ! {issue}");
        }

        foreach (PersonaIssue issue in result.Errors)
        {
            Console.WriteLine($"  ✗ {issue}");
        }

        if (!result.IsValid)
        {
            Console.WriteLine();
            Console.WriteLine("  persona is INVALID — the bot would refuse to boot on this.");
            return Task.FromResult(1);
        }

        Console.WriteLine("  ✓ persona is valid");
        return Task.FromResult(0);
    }

    /// <summary>
    /// Finds and loads the persona. Shared with <c>sonarr health</c>, which grades the same
    /// result without printing it. Resolution: <c>--dir</c>, else <c>PERSONA_PATH</c> from the
    /// environment, else walking up from the working directory for a <c>persona/</c> that holds
    /// <c>sonarr.yaml</c> (repo root on a dev box, /opt/sonarr on the server).
    /// </summary>
    public static (string Root, PersonaValidationResult Result) Load(string? dirFlag)
    {
        string root = ResolveRoot(dirFlag);
        return (root, PersonaLoader.Load(new DirectoryPersonaSource(root)));
    }

    private static string ResolveRoot(string? dirFlag)
    {
        if (!string.IsNullOrWhiteSpace(dirFlag))
        {
            return Directory.Exists(dirFlag)
                ? dirFlag
                : throw new CliError($"--dir points at '{dirFlag}', which is not a directory.", 2);
        }

        string? fromEnv = Environment.GetEnvironmentVariable("PERSONA_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        DirectoryInfo? walk = new(Directory.GetCurrentDirectory());
        while (walk is not null)
        {
            string candidate = Path.Combine(walk.FullName, "persona");
            if (File.Exists(Path.Combine(candidate, "sonarr.yaml")))
            {
                return candidate;
            }

            walk = walk.Parent;
        }

        throw new CliError(
            "no persona directory found — walked up from the working directory looking for "
            + "persona/sonarr.yaml. Point --dir or PERSONA_PATH at it.", 1);
    }
}
