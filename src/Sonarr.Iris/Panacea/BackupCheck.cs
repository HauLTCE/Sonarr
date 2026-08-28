using System.Diagnostics;
using System.Globalization;

using Npgsql;

using Sonarr.Domain.Jobs;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// The backup tree, graded the way <c>sonarr backups</c> grades it: something there, fresh,
/// and restorable-shaped. The remedy is the one a person would reach for — take a dump now —
/// running the same <c>pg_dump</c> protocol the nightly job uses (custom format, written under
/// <c>.partial</c>, verified, then renamed), so a healed backup row is a backup that restores.
/// </summary>
internal sealed class BackupCheck : IDoctorCheck
{
    /// <summary>How long the doctor's <c>pg_dump</c> gets — the nightly job allows thirty.</summary>
    private static readonly TimeSpan DumpTimeout = TimeSpan.FromMinutes(10);

    public string Name => "backups";

    public Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        try
        {
            string dir = BackupsCommand.FindDir(null);
            BackupReport report = BackupsCommand.Inspect(dir, DateOnly.FromDateTime(DateTime.Today));

            if (report.Dumps.Count == 0)
            {
                return Task.FromResult(Diagnosis.Broken(
                    $"no dumps found in {dir}",
                    "the nightly job has never written here — re-run this command to take a dump "
                    + "now, and check the bot's backup runner is scheduled"));
            }

            if (report.NewestDumpProblem is { } problem)
            {
                return Task.FromResult(Diagnosis.Broken(
                    $"newest dump is not restorable-shaped: {problem}",
                    "re-run this command — the doctor takes a fresh dump and verifies it before "
                    + "it keeps its name, the way the nightly job does"));
            }

            if (report.Stale)
            {
                return Task.FromResult(Diagnosis.Broken(
                    $"newest dump is {Output.LocalDate(report.Dumps[^1].Day)} — the nightly job is not running",
                    "re-run this command to take a dump now; then restore the nightly schedule "
                    + "(the bot writes one at 03:30 while it is up)"));
            }

            return Task.FromResult(Diagnosis.Ok(
                $"{Output.Count(report.Dumps.Count, "dump", "dumps")}, "
                + $"newest {Output.LocalDate(report.Dumps[^1].Day)}"));
        }
        catch (CliError ex)
        {
            return Task.FromResult(Diagnosis.Broken(
                Doctor.OneLine(ex.Message),
                "set BACKUP_PATH to where the nightly job writes, or create the tree — the "
                + "doctor will take a dump into it once it exists"));
        }
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        string dir;
        try
        {
            dir = BackupsCommand.FindDir(null);
        }
        catch (CliError)
        {
            // No tree anywhere. A BACKUP_PATH that merely does not exist yet is safe to create;
            // inventing a location when nothing names one is not.
            string? fromEnv = Environment.GetEnvironmentVariable("BACKUP_PATH");
            if (string.IsNullOrWhiteSpace(fromEnv))
            {
                return ["no backup tree to write into, and BACKUP_PATH does not name one"];
            }

            Directory.CreateDirectory(fromEnv);
            dir = fromEnv;
        }

        if (PgDumpPath() is not { } pgDump)
        {
            return ["tried to take a dump now — pg_dump is not on PATH "
                + "(postgresql-client is missing where the CLI runs)"];
        }

        string? connection = CliHost.Configuration["PG_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connection))
        {
            return ["tried to take a dump now — that needs Postgres, and PG_CONNECTION is not set"];
        }

        try
        {
            string path = await DumpAsync(pgDump, dir, connection.Trim(), ct);
            return [$"took a dump now: {Path.GetFileName(path)} ({Size(path)})"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [$"tried to take a dump now — {Doctor.OneLine(ex.Message)}"];
        }
    }

    /// <summary>
    /// The nightly job's dump protocol, run by hand: <c>pg_dump -Fc</c> into <c>.partial</c>,
    /// verified against the <c>pg_restore</c> header, then renamed — the rename is the commit,
    /// so a killed dump can never masquerade as a backup.
    /// </summary>
    private static async Task<string> DumpAsync(
        string pgDump, string dir, string connection, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        string path = Path.Combine(
            dir, BackupRetention.RelativePath(
                BackupRetention.DumpPrefix, today, BackupRetention.DumpExtension));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + ".partial";
        NpgsqlConnectionStringBuilder pg = new(connection);

        ProcessStartInfo start = new(pgDump)
        {
            // ArgumentList, not a command line — the same reasoning as the nightly runner: a
            // password or a database name with a space is otherwise a quoting bug in a process
            // nobody is watching.
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

        // The password goes in the environment, never in the argument list.
        start.Environment["PGPASSWORD"] = pg.Password ?? "";

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("pg_dump did not start");

        try
        {
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);

            using CancellationTokenSource timeout = new(DumpTimeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stderr, stdout);

            if (process.ExitCode != 0)
            {
                // First line only: pg_dump repeats the connection details below it.
                throw new InvalidOperationException(
                    $"pg_dump exited {process.ExitCode}: {FirstLine(stderr.Result)}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Kill(process);
            throw new InvalidOperationException($"pg_dump exceeded {DumpTimeout.TotalMinutes:F0} min");
        }
        finally
        {
            if (!process.HasExited)
            {
                Kill(process);
            }
        }

        // Verified before it keeps its name, exactly as the nightly job does.
        byte[] header = new byte[BackupVerification.CustomFormatMagic.Length];
        using (FileStream file = File.OpenRead(partial))
        {
            int read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (BackupVerification.Check(file.Length, header.AsSpan(0, read)) is { } problem)
            {
                File.Delete(partial);
                throw new InvalidOperationException(problem);
            }
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>The same PATH lookup the bot's <c>BackupProbe</c> does at boot.</summary>
    private static string? PgDumpPath()
    {
        string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return paths
            .Select(dir => Path.Combine(dir, OperatingSystem.IsWindows() ? "pg_dump.exe" : "pg_dump"))
            .FirstOrDefault(File.Exists);
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
