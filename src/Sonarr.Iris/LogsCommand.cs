using Sonarr.Iris.Logging;

namespace Sonarr.Iris;

/// <summary>
/// <c>sonarr logs</c> — read what the service wrote through <see cref="IrisLogging"/>, without
/// knowing journald or owning root: the files, tailed from disk.
/// </summary>
/// <remarks>
/// <para>
/// The journal (<c>journalctl -u sonarr</c>) holds the console half; the rolling files hold the
/// same events on disk with a 14-day retention, and they are the half this command serves. It
/// works from any user that can read the directory, with the bot stopped, and with no privilege
/// — which is what makes it useful on the box itself and on a copied tree.
/// </para>
/// <para>
/// <c>--errors</c> scans the newest day's file for warning-and-worse records, continuation lines
/// (exception stacks) included. <c>--follow</c> is a poll, the same honest shape as
/// <c>sonarr chat</c>: there is no subscription from a file sink, so it re-reads every two
/// seconds and says so in the help.
/// </para>
/// </remarks>
internal static class LogsCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        int lines = cli.Int("lines", 50);
        if (lines < 1)
        {
            throw CliError.Usage("--lines wants at least 1.");
        }

        bool errors = cli.Has("errors");

        DirectoryInfo directory = ResolveDirectory(cli.Value("dir"));
        FileInfo file = LogFile.Newest(directory) ?? throw new CliError(
            $"no {IrisLogging.FilePrefix}*.log files in {directory.FullName} — the bot either "
            + "never ran here or its logs live somewhere else (--dir).", 1);

        if (errors)
        {
            // Streaming: the scan touches each line once, whatever the file's size.
            IReadOnlyList<string> found = LogFile.FilterElevated(File.ReadLines(file.FullName));
            Console.WriteLine($"  {file.Name}: {Output.Count(found.Count, "line", "lines")} at warning or worse");
            foreach (string line in found)
            {
                Console.WriteLine(line);
            }
        }
        else
        {
            foreach (string line in LogFile.Tail(file, lines))
            {
                Console.WriteLine(line);
            }
        }

        if (!cli.Has("follow", "f"))
        {
            return 0;
        }

        return await FollowAsync(directory, file);
    }

    /// <summary>
    /// Where the rolling files live: an explicit <c>--dir</c>, else the working directory's
    /// <c>logs/</c> (running the CLI from /opt/sonarr or the repo root), else the one next to the
    /// binary (the installed wrapper runs from wherever the person stood, so the app directory is
    /// the only constant — /opt/sonarr/app/../logs).
    /// </summary>
    private static DirectoryInfo ResolveDirectory(string? dirFlag)
    {
        if (!string.IsNullOrWhiteSpace(dirFlag))
        {
            DirectoryInfo named = new(dirFlag);
            return named.Exists
                ? named
                : throw new CliError($"--dir points at '{dirFlag}', which is not a directory.", 2);
        }

        string[] candidates =
        [
            Path.Combine(Directory.GetCurrentDirectory(), IrisLogging.LogDirectory),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", IrisLogging.LogDirectory)),
        ];

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return new DirectoryInfo(candidate);
            }
        }

        throw new CliError(
            "no logs directory found — looked at " + string.Join(" and ", candidates)
            + ". Point --dir at it.", 1);
    }

    /// <summary>
    /// Prints lines appended to the newest file, every two seconds, until Ctrl-C. Re-discovers the
    /// newest file each poll: at midnight the sink rolls to a new name, and following is following
    /// the log, not one file.
    /// </summary>
    /// <remarks>
    /// Ctrl-C is caught so stopping a follow exits 0 — same rule as <c>sonarr chat</c>.
    /// </remarks>
    private static async Task<int> FollowAsync(DirectoryInfo directory, FileInfo current)
    {
        using CancellationTokenSource stop = new();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = !stop.IsCancellationRequested;
            stop.Cancel();
        };

        Console.CancelKeyPress += onCancel;
        Console.WriteLine();
        Console.WriteLine("  …following, every 2s. Ctrl-C to stop.");

        string name = current.Name;
        long position = current.Length;

        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);

                FileInfo? newest = LogFile.Newest(directory);
                if (newest is null)
                {
                    continue;
                }

                if (newest.Name != name)
                {
                    // Rolled: the new file starts empty, pick it up from the top.
                    name = newest.Name;
                    position = 0;
                }

                newest.Refresh();
                if (newest.Length < position)
                {
                    // Truncated under us — start over rather than reading from a dead offset.
                    position = 0;
                }

                if (newest.Length == position)
                {
                    continue;
                }

                using FileStream stream = newest.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(position, SeekOrigin.Begin);
                using StreamReader reader = new(stream);
                Console.Write(reader.ReadToEnd());
                position = stream.Length;
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C. Expected, and the only way out of the loop.
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        return 0;
    }
}
