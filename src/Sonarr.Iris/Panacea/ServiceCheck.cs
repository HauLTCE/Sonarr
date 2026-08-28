namespace Sonarr.Iris.Panacea;

/// <summary>
/// A systemd unit, examined with <c>systemctl is-active</c> and treated with
/// <c>systemctl start</c>. The bot itself is a host service (deploy/sonarr.service), so "the bot
/// is not running" is the most literal problem the doctor can be asked about — and the one an
/// operator most wants fixed rather than described.
/// </summary>
/// <remarks>
/// Deliberately narrow: <em>start a unit that is installed and not running</em>. A unit that
/// keeps failing is reported with where its reason is, not restarted in a loop — five failures
/// in a minute is the crashloop the unit file's StartLimitBurst already refuses, and a doctor
/// that fought it would only hide it. On a box with no systemd (a dev machine, a container) the
/// check reports blocked rather than red: there is no unit to be wrong about.
/// </remarks>
internal sealed class ServiceCheck(string unit, string what) : IDoctorCheck
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Set by the examination so the treatment does not have to re-read the state.</summary>
    private bool _failing;

    public string Name => what;

    public async Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            return Diagnosis.Blocked("no systemd here — this check is for the server");
        }

        // `is-active` exits non-zero for an inactive unit and still prints the state, so the
        // state is the answer and the exit code is not.
        CommandResult result = await Command.RunAsync(
            "systemctl", ["is-active", unit], CommandTimeout, ct);

        if (!result.Started || result.TimedOut)
        {
            return Diagnosis.Blocked("systemctl is not available");
        }

        string state = result.Stdout.Trim();
        _failing = state == "failed";

        return state switch
        {
            "active" => Diagnosis.Ok($"{unit} is active"),
            "failed" => Diagnosis.Broken(
                $"{unit} has failed",
                $"read `journalctl -u {unit} -n 50` — it started and died, so starting it again "
                + "would only hide the reason"),
            "inactive" => Diagnosis.Broken(
                $"{unit} is stopped",
                $"start it with `sudo systemctl start {unit}` — the doctor tries this itself, "
                + "and needs root to succeed"),
            "" => Diagnosis.Broken(
                $"{unit} is not installed",
                "run deploy/install-host.sh — the unit file is not on this box"),
            _ => Diagnosis.Broken($"{unit} is {state}", $"check `systemctl status {unit}`"),
        };
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        if (_failing)
        {
            return [$"left {unit} alone — it is failing, not merely stopped"];
        }

        CommandResult result = await Command.RunAsync("systemctl", ["start", unit], CommandTimeout, ct);
        if (!result.Started || result.TimedOut)
        {
            return [$"tried to start {unit} — systemctl is not available"];
        }

        if (result.Exit != 0)
        {
            return [$"tried to start {unit} — {Doctor.OneLine(result.Stderr.Trim().Length > 0
                ? result.Stderr
                : $"systemctl start exited {result.Exit}")} "
                + "(this needs root, or the unit is not installed)"];
        }

        // A moment to come up before the re-examination reads its state.
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        return [$"started {unit}"];
    }
}
