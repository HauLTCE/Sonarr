using SysProcess = System.Diagnostics.Process;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// What running an external command produced. <see cref="Started"/> is false when the binary
/// could not be launched at all — a different failure from one that ran and exited non-zero,
/// and the one the report must not describe as "the command failed".
/// </summary>
internal sealed record CommandResult(
    bool Started, bool TimedOut, int Exit, string Stdout, string Stderr, string? Error)
{
    public static CommandResult NotStarted(string error) =>
        new(false, false, -1, "", "", error);

    public static CommandResult Timeout() => new(true, true, -1, "", "", null);
}

/// <summary>
/// One place that launches a child process, drains both its pipes and honours a timeout. Every
/// remedy that shells out goes through here: docker, systemctl, pg_dump's cousins, the model
/// fetch. Doing it once means the deadlock (a full stderr pipe against a blocking wait) and the
/// wedged-process cap are got right once.
/// </summary>
internal static class Command
{
    /// <summary>Output cap per stream. A report has no use for megabytes of a build log.</summary>
    private const int MaxCapturedChars = 4000;

    public static async Task<CommandResult> RunAsync(
        string file,
        string[] arguments,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        System.Diagnostics.ProcessStartInfo start = new(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // Secrets go here, never in the argument list — /proc/*/cmdline is world-readable, and
        // this is the mechanism pg_dump's PGPASSWORD relies on.
        foreach ((string key, string value) in environment ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        SysProcess process;
        try
        {
            process = SysProcess.Start(start)
                ?? throw new InvalidOperationException("the process did not start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return CommandResult.NotStarted("not found or not runnable");
        }

        using (process)
        {
            // Both pipes drained concurrently with the wait: a child writing enough to fill one
            // pipe buffer would otherwise deadlock against WaitForExitAsync.
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

            using CancellationTokenSource cap = new(timeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, cap.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(process);
                return CommandResult.Timeout();
            }

            await Task.WhenAll(stdout, stderr);
            return new CommandResult(
                Started: true,
                TimedOut: false,
                Exit: process.ExitCode,
                Stdout: Cap(stdout.Result),
                Stderr: Cap(stderr.Result),
                Error: null);
        }
    }

    private static string Cap(string text) =>
        text.Length > MaxCapturedChars ? text[..MaxCapturedChars] : text;

    private static void Kill(SysProcess process)
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
