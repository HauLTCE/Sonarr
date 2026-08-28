namespace Sonarr.Iris.Panacea;

/// <summary>
/// <c>sonarr health</c> — the doctor's round. Examines everything the bot stands on (the .env,
/// Postgres, the schema, Redis, the persona, the backup tree), treats what it can, and files a
/// case per check: healthy, healed (what was broken and what fixed it), or still broken with
/// what was tried and what a person should do next.
/// </summary>
/// <remarks>
/// Default is examine-and-treat — the user asked for a doctor that actively fixes, not a
/// report. <c>--check</c> is the examination-only mode and keeps the old canary contract:
/// same checks, exit 0 only when everything passes, no remedy attempted, which is what makes
/// it usable from cron. Exit codes otherwise are unchanged: 0 when nothing is still broken
/// after treatment, 1 when something is.
/// </remarks>
internal static class PanaceaCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        bool heal = !cli.Has("check");
        List<IDoctorCheck> checks =
        [
            new EnvCheck(),
            new PostgresCheck(),
            new SchemaCheck(),
            new RedisCheck(),
            new PersonaCheck(),
            new BackupCheck(),
        ];

        List<CaseFile> files = await Doctor.RunAsync(checks, heal);

        Output.Heading(heal ? "Panacea — the doctor is in" : "Panacea — examination only (--check)");
        int width = files.Max(f => f.Name.Length);
        foreach (CaseFile file in files)
        {
            Render(file, width, heal);
        }

        int broken = files.Count(f => f.StillBroken);
        int healed = files.Count(f => f.Healed);
        Console.WriteLine();
        Console.WriteLine(broken == 0
            ? healed == 0
                ? "  ✓ all checks passed"
                : $"  ✓ healthy — {Output.Count(healed, "ailment", "ailments")} healed on the round"
            : $"  ✗ {Output.Count(broken, "ailment", "ailments")} the doctor could not fix"
                + (healed > 0 ? $" ({Output.Count(healed, "ailment", "ailments")} healed)" : ""));
        return broken == 0 ? 0 : 1;
    }

    /// <summary>
    /// One case. A healthy or blocked check is its glyph and one line; a treated check keeps
    /// its whole visit — the ailment it came in with, each step the doctor tried, and the
    /// outcome, with the operator-facing advice under a verdict that is still red.
    /// </summary>
    private static void Render(CaseFile file, int width, bool heal)
    {
        string indent = new(' ', width + 4);

        switch (file)
        {
            case { Final.Kind: Verdict.Healthy, First.Kind: Verdict.Healthy }:
                Console.WriteLine($"  {file.Name.PadRight(width)}  ✓ {file.Final.Detail}");
                return;

            case { Final.Kind: Verdict.Blocked }:
                Console.WriteLine($"  {file.Name.PadRight(width)}  – {file.Final.Detail}");
                return;

            case { Healed: true }:
                Console.WriteLine($"  {file.Name.PadRight(width)}  ✗ {file.First.Detail}");
                foreach (string step in file.Attempted)
                {
                    Console.WriteLine($"{indent}⚕ {step}");
                }

                Console.WriteLine($"{indent}✓ healed");
                return;

            default:
                Console.WriteLine($"  {file.Name.PadRight(width)}  ✗ {file.First.Detail}");
                if (!heal)
                {
                    break;
                }

                if (file.Attempted.Count == 0)
                {
                    Console.WriteLine($"{indent}⚕ nothing tried — there is no automatic remedy");
                }
                else
                {
                    foreach (string step in file.Attempted)
                    {
                        Console.WriteLine($"{indent}⚕ {step}");
                    }
                }

                Console.WriteLine($"{indent}✗ still broken");
                break;
        }

        if (file.Advice is { } advice)
        {
            Console.WriteLine($"{indent}→ {advice}");
        }
    }
}
