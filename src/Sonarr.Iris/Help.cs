using System.Reflection;

namespace Sonarr.Iris;

/// <summary>
/// The help screen. Written by hand and kept to one screen — goal 5 asked for "short and
/// concise", and a generated wall of flags is neither.
/// </summary>
/// <remarks>
/// Two levels only: <c>sonarr help</c> lists the verbs, <c>sonarr help &lt;verb&gt;</c> explains one.
/// Anything that needs a third level of nesting is a command that should have been two commands.
/// </remarks>
public static class Help
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static void Print(string? verb)
    {
        switch (verb)
        {
            case "stats": Stats(); return;
            case "chat": Chat(); return;
            case "set": Set(); return;
            case "get": Get(); return;
            case "guilds": Guilds(); return;
            default: Summary(); return;
        }
    }

    private static void Summary() => Console.WriteLine(
        """
        sonarr — the bot's command line. Reads Postgres and Redis directly; works with the bot
        stopped.

          sonarr stats [--guild ID] [--days N]      command usage, activity and member growth
          sonarr chat -u NAME [--follow]            one person's conversation history, live by default
          sonarr set KEY VALUE [--guild ID]         a feature toggle or a config key
          sonarr get [KEY] [--guild ID]             what is set now, and where it came from
          sonarr guilds                             which servers are in the database

        Common flags:
          --guild ID    which server (default: SONARR_GUILD from .env; 0 = global)
          -h, --help    this screen; `sonarr help VERB` for one command

        Exit codes: 0 fine, 1 it failed, 2 you typed it wrong.
        """);

    private static void Stats() => Console.WriteLine(
        """
        sonarr stats [--guild ID] [--days N]

        What is stored, not what the process is holding: command counts, the hourly activity
        series and members-first-seen-per-day. The bot's own in-memory counters reset on restart
        and are not readable from here.

          --days N     window, default 30, capped at 400 (the retention ceiling)
          --guild ID   required in practice — stats are per-server, and 0 has none
        """);

    private static void Chat() => Console.WriteLine(
        """
        sonarr chat -u NAME [--guild ID] [--days N] [--limit N] [--follow|--no-follow]

        One person's side of the record: her registers and tier, the facts she is holding, and the
        episodes in order. Follows live by default — new episodes are printed as they land — so
        pass --no-follow for a one-shot dump you can pipe.

          -u, --user NAME   Discord handle, or a raw user id
          --days N          how far back, default 7
          --limit N         episodes to show, default 50
          --no-follow       print once and exit
          --interval N      seconds between polls while following, default 3

        It shows what she recorded, which is her own lines and the facts she was told while
        addressed — not a transcript of the channel. Nothing else is stored (docs/06).
        """);

    private static void Set() => Console.WriteLine(
        """
        sonarr set KEY VALUE [--guild ID]

        Feature toggles and config keys, same catalogs Discord's /feature and /config use.

          sonarr set sleep false            global, applies to every server
          sonarr set midday true --guild 123
          sonarr set xp_multiplier 2 --guild 123

        Toggles (sleep, midday, lunch are short for sleep_mode and midday_break) live in
        core.feature_flag, which is the only config table that can hold a global row — so
        --guild 0, the default, is a real setting rather than an error. Config keys cannot: they
        are per-server, and setting one without --guild is refused.

        One gap, because this process has no gateway: it cannot tell a channel id from a role id,
        so `set dj_role <a channel>` is accepted here and would be caught in Discord. Use
        /config set for channel and role keys if that matters.
        """);

    private static void Get() => Console.WriteLine(
        """
        sonarr get [KEY] [--guild ID]

        With no KEY: every feature toggle with its state and where that state came from (this
        server's row, the global row, or the built-in default), then the config keys that are set.

        With a KEY: just that one. Exits 1 when a config key is unset, so it works in a script.
        """);

    private static void Guilds() => Console.WriteLine(
        """
        sonarr guilds

        Every server in core.guild with its id, name and join date, plus its member count. Useful
        for finding the id the other commands want.
        """);
}
