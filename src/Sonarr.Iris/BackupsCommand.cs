using Sonarr.Domain.Jobs;

namespace Sonarr.Iris;

/// <summary>
/// The shape of what <see cref="BackupsCommand.Inspect"/> found, so <c>sonarr health</c> can run
/// the same inspection and grade it without printing anything.
/// </summary>
internal sealed record BackupReport(
    string Directory,
    IReadOnlyList<BackupFile> Dumps,
    IReadOnlyList<BackupFile> Configs,
    int Strays,
    IReadOnlyList<BackupFile> Expired,
    string? NewestDumpProblem,
    bool Stale);

/// <summary>
/// <c>sonarr backups</c> — report on what the nightly job (docs/11) has been writing, and with
/// <c>--verify</c> shape-check every dump the way <see cref="BackupVerification"/> does.
/// </summary>
/// <remarks>
/// <para>
/// The unit file sets <c>BACKUP_PATH=/opt/sonarr/backups</c>, but that path exists only inside
/// the unit's BindPaths namespace — from a shell the files are at <c>/root/backups/sonarr</c>.
/// Resolution is therefore: <c>--dir</c>, else <c>BACKUP_PATH</c> if it exists, else the real
/// server path.
/// </para>
/// <para>
/// This is a read, never a prune: the retention math comes from <see cref="BackupRetention"/> so
/// the report and the job agree, but the delete loop belongs to the job alone.
/// </para>
/// </remarks>
internal static class BackupsCommand
{
    /// <summary>How many days late the newest dump may be before the report goes red (nightly 03:30).</summary>
    private const int StaleAfterDays = 2;

    public static Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        string dir = FindDir(cli.Value("dir"));
        BackupReport report = Inspect(dir, DateOnly.FromDateTime(DateTime.Today));

        Output.Heading($"Backups — {report.Directory}");
        Output.Rows(
        [
            ("dumps", SetSummary(report.Dumps)),
            ("configs", SetSummary(report.Configs)),
            ("strays", Output.Count(report.Strays, "file", "files") + " not named by the retention rules"),
            ("expired", report.Expired.Count == 0
                ? "none due for pruning"
                : $"{Output.Count(report.Expired.Count, "file", "files")} due for pruning"),
        ]);

        bool verify = cli.Has("verify");
        IEnumerable<BackupFile> toCheck = verify ? report.Dumps : report.Dumps.TakeLast(1);
        List<(string Name, string Verdict)> verdicts =
        [
            .. toCheck.Select(d => (
                Relative(report.Directory, d.Path),
                CheckDump(d.Path) is { } problem
                    ? problem
                    : OkWithSize(d.Path))),
        ];

        if (verdicts.Count > 0)
        {
            Output.Heading(verify ? "Verify — every dump" : "Newest dump");
            Output.Rows([.. verdicts]);
        }

        // Red conditions, each named so the exit code is not the only message.
        List<string> problems = [];
        if (report.Dumps.Count == 0)
        {
            problems.Add("no dumps at all — the nightly job has never written here");
        }
        else
        {
            if (report.NewestDumpProblem is not null)
            {
                problems.Add($"newest dump is not restorable-shaped: {report.NewestDumpProblem}");
            }

            if (report.Stale)
            {
                problems.Add(
                    $"newest dump is {Output.LocalDate(report.Dumps[^1].Day)} — more than "
                    + $"{StaleAfterDays} days old, the nightly job is not running");
            }
        }

        if (problems.Count > 0)
        {
            foreach (string problem in problems)
            {
                Console.WriteLine($"  ✗ {problem}");
            }

            return Task.FromResult(1);
        }

        Console.WriteLine($"  ✓ backups healthy ({Output.Zone})");
        return Task.FromResult(0);
    }

    /// <summary>
    /// Where the backup tree lives: an explicit <c>--dir</c>, else <c>BACKUP_PATH</c> if that
    /// directory exists (it does inside the unit's namespace, not from a shell), else the real
    /// server path.
    /// </summary>
    public static string FindDir(string? dirFlag)
    {
        if (!string.IsNullOrWhiteSpace(dirFlag))
        {
            return Directory.Exists(dirFlag)
                ? dirFlag
                : throw new CliError($"--dir points at '{dirFlag}', which is not a directory.", 2);
        }

        string? fromEnv = Environment.GetEnvironmentVariable("BACKUP_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        const string serverPath = "/root/backups/sonarr";
        if (Directory.Exists(serverPath))
        {
            return serverPath;
        }

        throw new CliError(
            "no backup directory found — BACKUP_PATH is unset or missing and " + serverPath
            + " does not exist. Point --dir at the tree.", 1);
    }

    /// <summary>
    /// Walks the tree once and grades it: what parsed as ours, what did not, what the retention
    /// rules would prune today, and whether the newest dump is restorable-shaped and fresh.
    /// </summary>
    public static BackupReport Inspect(string dir, DateOnly today)
    {
        List<BackupFile> dumps = [];
        List<BackupFile> configs = [];
        int strays = 0;

        foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            BackupFile? parsed = BackupRetention.Parse(path);
            if (parsed is null)
            {
                strays++;
            }
            else if (parsed.Prefix == BackupRetention.DumpPrefix)
            {
                dumps.Add(parsed);
            }
            else if (parsed.Prefix == BackupRetention.ConfigPrefix)
            {
                configs.Add(parsed);
            }
            else
            {
                strays++;
            }
        }

        dumps.Sort((a, b) => a.Day.CompareTo(b.Day));
        configs.Sort((a, b) => a.Day.CompareTo(b.Day));

        BackupFile? newest = dumps.LastOrDefault();
        return new BackupReport(
            dir,
            dumps,
            configs,
            strays,
            BackupRetention.Expired(dumps.Concat(configs), today),
            newest is null ? null : CheckDump(newest.Path),
            newest is not null && today.DayNumber - newest.Day.DayNumber > StaleAfterDays);
    }

    /// <summary>
    /// The <see cref="BackupVerification"/> shape check against one file: its size plus the first
    /// bytes of its header. Null when restorable-shaped, else the clause for the red line.
    /// </summary>
    private static string? CheckDump(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] header = new byte[BackupVerification.CustomFormatMagic.Length];
            int read = stream.Read(header);
            return BackupVerification.Check(stream.Length, header.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"unreadable: {ex.Message}";
        }
    }

    private static string SetSummary(IReadOnlyList<BackupFile> set)
        => set.Count == 0
            ? "none"
            : $"{Output.Count(set.Count, "file", "files")}, "
                + $"{Output.LocalDate(set[0].Day)} … {Output.LocalDate(set[^1].Day)}";

    /// <summary>The ok verdict with the file's size, when the size is readable.</summary>
    private static string OkWithSize(string path)
    {
        try
        {
            return $"ok ({Size(new FileInfo(path).Length)})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "ok";
        }
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => FormattableString.Invariant($"{bytes / (double)(1L << 30):0.0} GB"),
        >= 1L << 20 => FormattableString.Invariant($"{bytes / (double)(1L << 20):0.0} MB"),
        >= 1L << 10 => FormattableString.Invariant($"{bytes / (double)(1L << 10):0.0} KB"),
        _ => FormattableString.Invariant($"{bytes} B"),
    };
}
