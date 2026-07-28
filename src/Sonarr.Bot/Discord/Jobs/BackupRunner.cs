using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using Npgsql;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Jobs;

namespace Sonarr.Bot.Discord.Jobs;

/// <summary>
/// The nightly dump (docs/08, 03:30) and the weekly config archive, into
/// <c>{BackupPath}/YYYY/MM/</c> — plus the retention prune of both sets (docs/11).
/// </summary>
/// <remarks>
/// <para>A timer rather than a <c>core.job</c> row, for the same reason as
/// <see cref="RetentionPruner"/>: a missed backup costs a day, and tomorrow's run needs no memory of
/// today's. 03:30 is half an hour before the retention sweep on purpose — the dump should capture the
/// rows the sweep is about to delete, not the state after.</para>
/// <para><b>Why this shells out.</b> There is no in-process equivalent: <c>pg_dump</c> is the only
/// thing that writes a <c>pg_restore</c>-compatible archive, and a restore drill that needs a custom
/// tool is a restore drill that fails. The binary comes from <c>postgresql-client</c> in the runtime
/// image; <see cref="BackupProbe"/> turns a missing one into a red check rather than a surprise at
/// 03:30.</para>
/// <para><b>Why no gzip.</b> docs/11 says <c>pg_dump -Fc | gzip</c>, and <c>-Fc</c> is already
/// zlib-compressed — piping it through gzip costs CPU on a J2900 to add about nothing, and the
/// double extension invites a restore drill that gunzips first and then wonders why. The dump is
/// <c>.dump</c>, and <see cref="BackupVerification"/> checks for the <c>PGDMP</c> header that
/// <c>pg_restore</c> needs. The config archive <em>is</em> <c>.tar.gz</c> — that one is text.</para>
/// <para><b>The failure path is a red self-test check</b>, not a direct channel post: docs/08 wants
/// a red line in the log channel on failure, and <see cref="ISelfTest"/> already owns that line and
/// its change-detection. Riding it means a broken backup shows up in <c>/status</c> too, and cannot
/// produce a nightly wall of identical alarms.</para>
/// </remarks>
public sealed class BackupRunner(
    SonarrOptions options,
    BackupState state,
    ILogger<BackupRunner> log) : BackgroundService
{
    /// <summary>docs/08: 03:30, before the 04:00 retention sweep.</summary>
    public static readonly TimeOnly RunAt = new(3, 30);

    /// <summary>How long <c>pg_dump</c> gets. Minutes at this scale; this is the wedged-process cap.</summary>
    public static readonly TimeSpan DumpTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The config archive is weekly (docs/11). Monday, so it lands on the same file every week and
    /// <see cref="BackupRetention"/> sees one archive per week rather than a drifting date.
    /// </summary>
    public const DayOfWeek ConfigDay = DayOfWeek.Monday;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("BackupRunner running daily at {RunAt} into {Path}", RunAt, options.BackupPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DailySchedule.UntilNextRun(DateTimeOffset.Now, RunAt), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One backup pass. Public so a test can drive it without waiting for 03:30, same as
    /// <see cref="RetentionPruner.SweepAsync"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.Now.Date);

        // Three independent steps, three try blocks: a failed config archive must not cost the
        // database dump, and a failed prune must not hide the fact that tonight's dump succeeded.
        try
        {
            string path = await DumpAsync(today, ct).ConfigureAwait(false);
            state.Report(true, $"{Path.GetFileName(path)}, {Size(path)}");
            log.LogInformation("Backup: wrote {File} ({Size})", Path.GetFileName(path), Size(path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message, not the exception, when we produced it ourselves — a Postgres exception's
            // text can carry the connection string (docs/06: secrets never reach a sink).
            state.Report(false, ex is BackupFailedException ? ex.Message : ex.GetType().Name);
            log.LogError(ex, "Backup failed; retrying tomorrow.");
        }

        if (today.DayOfWeek == ConfigDay)
        {
            try
            {
                string path = ArchiveConfig(today);
                log.LogInformation("Backup: wrote {File} ({Size})", Path.GetFileName(path), Size(path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not a red check: the dump is the backup that matters, and the persona directory is
                // in git. Losing the weekly .env copy is a log line, not an alarm.
                log.LogError(ex, "Config archive failed; retrying next week.");
            }
        }

        try
        {
            Prune(today);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Backup prune failed; retrying tomorrow.");
        }
    }

    /// <summary>
    /// <c>pg_dump -Fc</c> into today's file, verified before it is allowed to keep its name.
    /// </summary>
    private async Task<string> DumpAsync(DateOnly today, CancellationToken ct)
    {
        string path = Path.Combine(
            options.BackupPath,
            BackupRetention.RelativePath(BackupRetention.DumpPrefix, today, BackupRetention.DumpExtension));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written under .partial and renamed only once verified, so a killed pg_dump can never leave
        // something that looks like last night's backup. The rename is the commit.
        string partial = path + ".partial";
        NpgsqlConnectionStringBuilder pg = new(options.PgConnection);

        ProcessStartInfo start = new("pg_dump")
        {
            // ArgumentList, not a command line: a password or a database name with a space would
            // otherwise be a quoting bug at 03:30 in a process nobody is watching.
            ArgumentList =
            {
                "--format=custom",
                "--no-owner",
                "--no-privileges",
                "--file=" + partial,
                "--host=" + pg.Host,
                "--port=" + pg.Port.ToString(CultureInfo.InvariantCulture),
                "--username=" + pg.Username,
                "--dbname=" + pg.Database,
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // The password goes in the environment, never in the argument list — /proc/*/cmdline is
        // world-readable, and this process shares a host with the old bot.
        start.Environment["PGPASSWORD"] = pg.Password ?? "";

        using Process process = Process.Start(start)
            ?? throw new BackupFailedException("pg_dump did not start");

        try
        {
            // Both pipes drained concurrently with the wait: pg_dump writing enough to stderr to
            // fill the pipe buffer would otherwise deadlock against WaitForExitAsync.
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);

            using CancellationTokenSource timeout = new(DumpTimeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stderr, stdout).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                // First line only: pg_dump's later lines repeat the connection details.
                throw new BackupFailedException(
                    $"pg_dump exited {process.ExitCode}: {FirstLine(stderr.Result)}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new BackupFailedException($"pg_dump exceeded {DumpTimeout.TotalMinutes:F0} min");
        }
        finally
        {
            if (!process.HasExited)
            {
                Kill(process);
            }
        }

        Verify(partial);

        // Overwrite: a same-day re-run should replace its own file, not fail on it.
        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>Deletes the file and throws if it is not restorable-shaped (docs/08).</summary>
    private static void Verify(string path)
    {
        long bytes = new FileInfo(path).Length;

        byte[] header = new byte[BackupVerification.CustomFormatMagic.Length];
        using (FileStream file = File.OpenRead(path))
        {
            int read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (BackupVerification.Check(bytes, header.AsSpan(0, read)) is { } problem)
            {
                // Deleted, not kept: a file that fails the header check is not a backup, and leaving
                // it means the next prune counts it as one.
                File.Delete(path);
                throw new BackupFailedException(problem);
            }
        }
    }

    /// <summary>
    /// The weekly persona + <c>.env</c> archive (docs/11: "secrets are part of disaster recovery").
    /// <c>TarFile</c> + <c>GZipStream</c> rather than shelling out to tar — both are in the BCL.
    /// </summary>
    private string ArchiveConfig(DateOnly today)
    {
        string path = Path.Combine(
            options.BackupPath,
            BackupRetention.RelativePath(
                BackupRetention.ConfigPrefix, today, BackupRetention.ConfigExtension));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string partial = path + ".partial";
        using (FileStream file = File.Create(partial))
        using (GZipStream gzip = new(file, CompressionLevel.Optimal))
        using (TarWriter tar = new(gzip))
        {
            foreach (string entry in Directory.EnumerateFiles(
                options.PersonaPath, "*", SearchOption.AllDirectories))
            {
                tar.WriteEntry(entry, "persona/" + Path.GetRelativePath(options.PersonaPath, entry)
                    .Replace('\\', '/'));
            }

            // The .env sits next to the compose file on the host, not in the container — it reaches
            // this process as environment variables, so there is no file to copy. Reconstructed from
            // the options instead, which is the same content and cannot drift from what booted.
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, ".env")
            {
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(EnvFile())),

                // 0600: the archive holds the bot token and the Lavalink password. A restore that
                // widens those is a restore that leaks them.
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// The running configuration as an <c>.env</c>, for the archive only. Never logged, never served.
    /// </summary>
    private string EnvFile() => string.Join('\n',
    [
        "# Reconstructed by BackupRunner from the running process. Secrets included on purpose",
        "# (docs/11 — disaster recovery). Treat this file as the token itself.",
        "DISCORD_TOKEN=" + options.DiscordToken,
        "LAVALINK_URI=" + options.LavalinkUri,
        "LAVALINK_PASSWORD=" + options.LavalinkPassword,
        "PG_CONNECTION=" + options.PgConnection,
        "REDIS_CONNECTION=" + options.RedisConnection,
        "ADMIN_USER_IDS=" + string.Join(',', options.AdminUserIds),
        "PANEL_BASE_URL=" + options.PanelBaseUrl,
        "",
    ]);

    /// <summary>Applies <see cref="BackupRetention"/> to what is actually on disk.</summary>
    private void Prune(DateOnly today)
    {
        if (!Directory.Exists(options.BackupPath))
        {
            return;
        }

        BackupFile[] found =
        [
            .. Directory.EnumerateFiles(options.BackupPath, "*", SearchOption.AllDirectories)
                .Select(BackupRetention.Parse)
                .OfType<BackupFile>(),
        ];

        int deleted = 0;
        foreach (BackupFile expired in BackupRetention.Expired(found, today))
        {
            try
            {
                File.Delete(expired.Path);
                deleted++;
            }
            catch (IOException ex)
            {
                log.LogWarning(ex, "Could not delete expired backup {File}", Path.GetFileName(expired.Path));
            }
        }

        if (deleted > 0)
        {
            log.LogInformation("Backup: pruned {Deleted} of {Found} file(s).", deleted, found.Length);
        }
    }

    private static string Size(string path)
        => $"{new FileInfo(path).Length / 1024d / 1024d:F1} MB";

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();
        int newline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        return newline < 0 ? trimmed : trimmed[..newline];
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill. Nothing to do.
        }
    }
}

/// <summary>A backup failure we diagnosed ourselves, safe to put in a log line and a red check.</summary>
public sealed class BackupFailedException(string message) : Exception(message);

/// <summary>
/// Last night's verdict, shared between <see cref="BackupRunner"/> and <see cref="BackupProbe"/>.
/// A singleton holding two fields rather than an event: the probe only ever asks "what happened
/// last time", and the runner only ever answers.
/// </summary>
public sealed class BackupState
{
    private volatile Verdict? _last;

    /// <summary>Null until the first run — a bot up for an hour has not missed a backup yet.</summary>
    public (bool Healthy, string Detail, DateTimeOffset At)? Last
        => _last is { } v ? (v.Healthy, v.Detail, v.At) : null;

    public void Report(bool healthy, string detail)
        => _last = new Verdict(healthy, detail, DateTimeOffset.UtcNow);

    private sealed record Verdict(bool Healthy, string Detail, DateTimeOffset At);
}

/// <summary>
/// Puts the backup verdict in the self-test report, which is what produces docs/08's red line and
/// what <c>/status</c> renders. Also checks <c>pg_dump</c> exists at boot, so a runtime image built
/// without <c>postgresql-client</c> is red immediately rather than at 03:30.
/// </summary>
public sealed class BackupProbe(BackupState state, SonarrOptions options) : ISelfTestProbe
{
    public string Name => "Backups";

    public Task<SelfTestCheck> RunAsync(CancellationToken cancellationToken = default)
    {
        if (state.Last is { } last)
        {
            // Stale is red: "the last backup succeeded, 9 days ago" means the timer is dead.
            bool stale = DateTimeOffset.UtcNow - last.At > TimeSpan.FromDays(2);
            return Task.FromResult(new SelfTestCheck(
                Name,
                last.Healthy && !stale,
                stale ? $"last run {(DateTimeOffset.UtcNow - last.At).TotalDays:F0} d ago" : last.Detail));
        }

        return Task.FromResult(Missing() is { } problem
            ? new SelfTestCheck(Name, false, problem)
            : new SelfTestCheck(Name, true, $"scheduled {BackupRunner.RunAt}, none yet"));
    }

    /// <summary>What is missing before 03:30 can possibly work, or null if nothing is.</summary>
    private string? Missing()
    {
        if (!Directory.Exists(options.BackupPath))
        {
            return $"{options.BackupPath} is not mounted";
        }

        // PATH lookup by hand: Process.Start would tell us the same thing by throwing, but only at
        // 03:30, and only into a log.
        string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return paths.Any(dir => File.Exists(Path.Combine(dir, "pg_dump"))
                || File.Exists(Path.Combine(dir, "pg_dump.exe")))
            ? null
            : "pg_dump not on PATH (postgresql-client missing from the image)";
    }
}
