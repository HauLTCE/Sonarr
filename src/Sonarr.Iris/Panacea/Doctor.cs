using System.Text.RegularExpressions;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// Panacea — the doctor module behind <c>sonarr health</c> (named for the goddess of the
/// universal remedy). Every check is an examination first and a treatment second: she looks at
/// the real artifact, and when something is broken she tries to fix it before reporting. A
/// check that cannot be healed says what is broken, what was tried, and what a person should
/// do next — the three things an operator standing in front of a red screen needs.
/// </summary>
/// <remarks>
/// The doctor treats, so the examination must never be a liveness signal: checks run against
/// the real artifact (a connection, a ping, the loader, the files on disk), the same rule the
/// plain <c>sonarr health</c> canary always followed. A green liveness pulse next to a broken
/// bot has happened twice in this codebase already; see the note on <c>ExamineAsync</c>.
/// </remarks>
internal static partial class Doctor
{
    /// <summary>Runs every check: examine, treat when healing is on, re-examine, file the case.</summary>
    public static async Task<List<CaseFile>> RunAsync(
        IEnumerable<IDoctorCheck> checks, bool heal, CancellationToken ct = default)
    {
        List<CaseFile> files = [];
        foreach (IDoctorCheck check in checks)
        {
            Diagnosis first = await Safely(() => check.ExamineAsync(ct), check.Name, ct);

            List<string> attempted = [];
            Diagnosis final = first;
            if (heal && first.Kind == Verdict.Broken)
            {
                try
                {
                    attempted.AddRange(await check.TreatAsync(first, ct));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempted.Add($"treatment failed: {OneLine(ex.Message)}");
                }

                final = await Safely(() => check.ExamineAsync(ct), check.Name, ct);
            }

            files.Add(new CaseFile(check.Name, first, attempted, final));
        }

        return files;
    }

    /// <summary>
    /// One examination. Throwing is not an ailment the check gets to grade for itself, so the
    /// runner turns it into one: broken, with the message a person can read.
    /// </summary>
    private static async Task<Diagnosis> Safely(
        Func<Task<Diagnosis>> examine, string name, CancellationToken ct)
    {
        try
        {
            return await examine();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Diagnosis.Broken($"{name}: {OneLine(ex.Message)}");
        }
    }

    [GeneratedRegex("password=[^,;\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordFragment();

    /// <summary>
    /// One line of a failure message, capped, with any <c>password=…</c> fragment blanked:
    /// connection strings surface in provider exception text, and the .env rule is that
    /// secrets never leave the box.
    /// </summary>
    public static string OneLine(string message)
    {
        string line = PasswordFragment()
            .Replace(message.Split('\n')[0].Trim(), "password=***");
        return line.Length <= 120 ? line : line[..117] + "…";
    }
}

/// <summary>What an examination concluded.</summary>
internal enum Verdict
{
    /// <summary>Nothing wrong.</summary>
    Healthy,

    /// <summary>Broken, with what to do about it in <see cref="Diagnosis.Advice"/>.</summary>
    Broken,

    /// <summary>
    /// Could not be examined because something upstream is already broken (a schema check with
    /// no database). Rendered, but not a failure of its own — the upstream case carries it.
    /// </summary>
    Blocked,
}

/// <summary>
/// One examination's finding. <see cref="Advice"/> is the operator-facing sentence for a broken
/// verdict: what to do when the doctor could not fix it.
/// </summary>
internal sealed record Diagnosis(Verdict Kind, string Detail, string? Advice = null)
{
    public static Diagnosis Ok(string detail) => new(Verdict.Healthy, detail);

    public static Diagnosis Broken(string detail, string? advice = null) =>
        new(Verdict.Broken, detail, advice);

    public static Diagnosis Blocked(string reason) => new(Verdict.Blocked, reason);

    public bool Healthy => Kind == Verdict.Healthy;
}

/// <summary>
/// One check's whole visit: what the first examination found, what treatment was attempted, and
/// what the re-examination found afterwards. <see cref="Healed"/> is the difference between the
/// two examinations — the doctor's word for "was broken, treated, now fine".
/// </summary>
internal sealed record CaseFile(
    string Name, Diagnosis First, IReadOnlyList<string> Attempted, Diagnosis Final)
{
    public bool Healed => First.Kind == Verdict.Broken && Final.Healthy;

    public bool StillBroken => Final.Kind == Verdict.Broken;

    /// <summary>The advice to print, from whichever examination still holds the diagnosis.</summary>
    public string? Advice => Final.Advice ?? First.Advice;
}

/// <summary>
/// One thing the doctor can examine and, when it can, treat. Checks run in the order they are
/// handed to <see cref="Doctor.RunAsync"/>; each one gates itself on what it needs and reports
/// <see cref="Verdict.Blocked"/> rather than failing twice for one upstream cause.
/// </summary>
internal interface IDoctorCheck
{
    /// <summary>The row label, lowercase, the part of the system this check owns.</summary>
    string Name { get; }

    /// <summary>
    /// Look at the real artifact and say what is true. Must not be a liveness signal — see the
    /// module remarks. Called once before treatment and once after.
    /// </summary>
    Task<Diagnosis> ExamineAsync(CancellationToken ct);

    /// <summary>
    /// Try to fix what <paramref name="ailment"/> describes. Returns the steps attempted, in
    /// order — an empty list means there is no automatic remedy, and the diagnosis's advice
    /// stands. The runner re-examines afterwards; the treatment itself does not declare
    /// success or failure, the examination does.
    /// </summary>
    Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
