using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sonarr.Elaine.Persona;
using Sonarr.Infrastructure.Persistence;

using StackExchange.Redis;

namespace Sonarr.Iris;

/// <summary>
/// <c>sonarr health</c> — the four things the bot stands on, checked directly and independently:
/// Postgres, Redis, the persona on disk, and the backup tree. A row per check, exit 0 only when
/// all four pass.
/// </summary>
/// <remarks>
/// Every check runs against the real artifact, never a liveness signal: Postgres is connected to,
/// Redis is pinged, the persona is loaded through the same loader the bot uses, the backups are
/// the files the nightly job wrote. Each failure prints one line a person can act on, and the
/// command exits 1 — which is what makes it usable as a cron canary, not just a screen to read.
/// </remarks>
internal static partial class HealthCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        List<(string Name, bool Ok, string Detail)> rows =
        [
            await CheckPostgresAsync(),
            await CheckRedisAsync(),
            CheckPersona(),
            CheckBackups(),
        ];

        Output.Heading("Health");
        int width = rows.Max(r => r.Name.Length);
        foreach ((string name, bool ok, string detail) in rows)
        {
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {name.PadRight(width)}  {detail}");
        }

        int failed = rows.Count(r => !r.Ok);
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "  ✓ all checks passed"
            : $"  ✗ {Output.Count(failed, "check", "checks")} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<(string, bool, string)> CheckPostgresAsync()
    {
        try
        {
            using IServiceScope scope = CliHost.Scope();
            SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();
            bool ok = await db.Database.CanConnectAsync();
            return ("postgres", ok, ok ? "connected" : "reachable but the connection was refused");
        }
        catch (Exception ex)
        {
            return ("postgres", false, OneLine(ex.Message));
        }
    }

    private static async Task<(string, bool, string)> CheckRedisAsync()
    {
        try
        {
            using IServiceScope scope = CliHost.Scope();
            IConnectionMultiplexer redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            TimeSpan roundTrip = await redis.GetDatabase().PingAsync();
            return ("redis", true, $"{roundTrip.TotalMilliseconds.ToString("0.#")} ms");
        }
        catch (Exception ex)
        {
            return ("redis", false, OneLine(ex.Message));
        }
    }

    private static (string, bool, string) CheckPersona()
    {
        try
        {
            (_, PersonaValidationResult result) = PersonaCommand.Load(null);
            if (!result.IsValid)
            {
                return ("persona", false,
                    $"{Output.Count(result.Errors.Count(), "error", "errors")} — run `sonarr persona`");
            }

            PersonaGraph graph = result.Graph!;
            return ("persona", true, $"valid — {graph.Intents.Count} intents, {graph.Pools.Count} pools");
        }
        catch (CliError ex)
        {
            return ("persona", false, OneLine(ex.Message));
        }
    }

    private static (string, bool, string) CheckBackups()
    {
        try
        {
            string dir = BackupsCommand.FindDir(null);
            BackupReport report = BackupsCommand.Inspect(dir, DateOnly.FromDateTime(DateTime.Today));
            if (report.Dumps.Count == 0)
            {
                return ("backups", false, "no dumps found in " + dir);
            }

            if (report.NewestDumpProblem is { } problem)
            {
                return ("backups", false, $"newest dump is not restorable-shaped: {problem}");
            }

            if (report.Stale)
            {
                return ("backups", false,
                    $"newest dump is {Output.LocalDate(report.Dumps[^1].Day)} — the nightly job is not running");
            }

            return ("backups", true,
                $"{Output.Count(report.Dumps.Count, "dump", "dumps")}, "
                + $"newest {Output.LocalDate(report.Dumps[^1].Day)}");
        }
        catch (CliError ex)
        {
            return ("backups", false, OneLine(ex.Message));
        }
    }

    [GeneratedRegex("password=[^,;\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordFragment();

    /// <summary>
    /// One line of a failure message, capped, with any <c>password=…</c> fragment blanked:
    /// StackExchange.Redis puts its connection options in exception messages, and the .env rule
    /// is that secrets never leave the box.
    /// </summary>
    private static string OneLine(string message)
    {
        string line = PasswordFragment()
            .Replace(message.Split('\n')[0].Trim(), "password=***");
        return line.Length <= 120 ? line : line[..117] + "…";
    }
}
