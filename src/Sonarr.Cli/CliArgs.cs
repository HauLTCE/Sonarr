using System.Globalization;

namespace Sonarr.Cli;

/// <summary>
/// A failure with a sentence already written for the person who typed the command, and the exit
/// code that goes with it.
/// </summary>
/// <remarks>
/// Separate from a bare <see cref="Exception"/> so <c>Program</c> can tell "you asked for a guild
/// I am not in" (a 2 — your command line was wrong) from "Postgres refused the connection" (a 1 —
/// the command was fine, the world was not).
/// </remarks>
public sealed class CliError(string message, int exitCode = 1) : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    /// <summary>The command line was wrong — a missing flag, an unparseable value.</summary>
    public static CliError Usage(string message) => new(message, 2);
}

/// <summary>
/// The parsed command line. Flags only, no positional arguments beyond the verb — a CLI whose
/// third positional argument means something different per verb is a CLI nobody can remember.
/// </summary>
/// <remarks>
/// Deliberately not System.CommandLine: that package is a 500 KB dependency and a generated help
/// screen, and goal 5 asked for help that is "short and concise", which is a paragraph somebody
/// writes rather than something a library emits. The parse below is ~40 lines and handles what
/// this CLI has: <c>--flag value</c>, <c>--flag=value</c>, <c>-u value</c>, and bare switches.
/// </remarks>
public sealed class CliArgs
{
    private readonly Dictionary<string, string?> _flags = new(StringComparer.OrdinalIgnoreCase);

    public CliArgs(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Skips args[0] — that is the verb, and Program has already dispatched on it.
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            if (!IsFlag(arg))
            {
                Positional.Add(arg);
                continue;
            }

            string name = arg.TrimStart('-');
            int equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                _flags[name[..equals]] = name[(equals + 1)..];
                continue;
            }

            // A flag takes the next token as its value unless that token is another flag, which
            // is what makes a bare switch like `--no-follow` work without a "true" after it.
            bool valueFollows = i + 1 < args.Length && !IsFlag(args[i + 1]);
            _flags[name] = valueFollows ? args[++i] : null;
        }
    }

    /// <summary>
    /// A leading dash means a flag — unless what follows it is a number.
    /// </summary>
    /// <remarks>
    /// Without the number check <c>--days -1</c> parses as the bare switch <c>--days</c> followed by
    /// a flag called <c>1</c>, so <c>--days</c> silently falls back to its default and the
    /// "wants at least 1" guard downstream can never fire for the input it was written for. Making
    /// <c>-1</c> a value means that guard rejects it, which is the whole point of having it.
    /// </remarks>
    private static bool IsFlag(string arg)
        => arg.StartsWith('-')
            && arg.Length > 1
            && !char.IsAsciiDigit(arg[1]);

    /// <summary>Non-flag arguments after the verb, in the order they were typed.</summary>
    public List<string> Positional { get; } = [];

    /// <summary>True when the flag was present at all, with or without a value.</summary>
    public bool Has(params string[] names) => names.Any(_flags.ContainsKey);

    /// <summary>The flag's value, or null when it is absent or was a bare switch.</summary>
    public string? Value(params string[] names)
    {
        foreach (string name in names)
        {
            if (_flags.TryGetValue(name, out string? value) && value is { Length: > 0 })
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The flag's value, or <paramref name="fallback"/>. Errors on a value that is not a number.</summary>
    public int Int(string name, int fallback)
    {
        string? raw = Value(name);
        if (raw is null)
        {
            return fallback;
        }

        return int.TryParse(raw, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw CliError.Usage($"--{name} wants a number, not '{raw}'.");
    }

    /// <summary>
    /// The guild this command is about: <c>--guild</c>, else <c>SONARR_GUILD</c> from the
    /// environment or .env.
    /// </summary>
    /// <remarks>
    /// Zero is legal and is not a mistake — it is the global feature-flag row, which is the whole
    /// point of <c>sonarr set sleep false</c> being global (goal 5's second footnote). Callers that
    /// need a real guild say so themselves.
    /// </remarks>
    public ulong Guild()
    {
        string? raw = Value("guild", "g") ?? CliHost.Configuration["SONARR_GUILD"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        return ulong.TryParse(raw.Trim(), CultureInfo.InvariantCulture, out ulong id)
            ? id
            : throw CliError.Usage($"--guild wants a Discord id, not '{raw}'.");
    }
}
