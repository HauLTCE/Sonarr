namespace Sonarr.Domain.Chat;

/// <summary>
/// The hours she does not answer in, restored from the Python bot's <c>SLEEP_MODE_ENABLED</c> and
/// <c>MIDDAY_BREAK_ENABLED</c> (docs/10 — chat gates). Domain constants and one pure predicate, so
/// the pipeline, the CLI and the tests all quote the same window.
/// </summary>
/// <remarks>
/// <para>
/// The legacy pair gated <c>economy_allowed()</c> — the games and payouts. This rewrite has no
/// economy module (the only surviving mention is the legacy importer), so the toggles keep their
/// names and their windows but move to the surface that still exists: the chat reply path. "She is
/// asleep" and "she is at lunch" were always what the windows meant to the people in the server;
/// what changes is that they no longer stop a <c>!daily</c> nobody can run.
/// </para>
/// <para>
/// Both windows are read in the <em>guild's</em> zone, not the container's, for the same reason her
/// calendar is: the point is whether the people talking to her are awake. Hard-coded rather than
/// configurable, because the two legacy env vars were booleans — there was never an hour to set,
/// and inventing one now would be a feature the goal did not ask for.
/// </para>
/// </remarks>
public static class QuietHours
{
    /// <summary>Sleep starts at 22:00 local and runs to <see cref="SleepEndHour"/>.</summary>
    public const int SleepStartHour = 22;

    /// <summary>Sleep ends at 06:00 local — the first hour she answers again.</summary>
    public const int SleepEndHour = 6;

    /// <summary>The one hour the midday break covers: 12:00–12:59 local.</summary>
    public const int MiddayHour = 12;

    /// <summary>
    /// Whether <paramref name="local"/> falls in the sleep window. The comparison is an
    /// <c>or</c> rather than a range because the window wraps midnight.
    /// </summary>
    public static bool IsAsleep(DateTimeOffset local)
        => local.Hour >= SleepStartHour || local.Hour < SleepEndHour;

    /// <inheritdoc cref="MiddayHour"/>
    public static bool IsMiddayBreak(DateTimeOffset local) => local.Hour == MiddayHour;
}
