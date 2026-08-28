namespace Sonarr.Iris.Panacea;

/// <summary>
/// Turning a <see cref="Command"/> run into report steps. The report gets a sentence per
/// outcome, not a transcript: a fetch script's progress bars and a package manager's dependency
/// tree are noise in a health report, so only the last few lines survive a failure and none
/// survive a success.
/// </summary>
internal static class Shell
{
    /// <summary>
    /// Runs <paramref name="file"/> and returns what to print. Never throws: a command that
    /// cannot start, times out, or exits non-zero is a step that says so.
    /// </summary>
    public static async Task<List<string>> RunAsync(
        string file, string[] arguments, TimeSpan timeout, CancellationToken ct)
    {
        CommandResult result = await Command.RunAsync(file, arguments, timeout, ct);

        if (!result.Started)
        {
            return [$"could not run {file} — {result.Error}"];
        }

        if (result.TimedOut)
        {
            return [$"{file} did not finish within {timeout.TotalMinutes:F0} min"];
        }

        if (result.Exit == 0)
        {
            return ["done"];
        }

        List<string> lines = [$"{file} exited {result.Exit}"];
        lines.AddRange(LastLines(result.Stderr.Length > 0 ? result.Stderr : result.Stdout, 3));
        return lines;
    }

    /// <summary>The last few non-blank lines, each trimmed to report width.</summary>
    private static IEnumerable<string> LastLines(string text, int count) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(count)
            .Select(Doctor.OneLine);
}
