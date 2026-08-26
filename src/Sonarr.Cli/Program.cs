using Sonarr.Cli;

// The `sonarr` CLI (goal 5). One-shot console app in the shape of Sonarr.Migrator: verb first,
// flags after, exit code says what happened.
//
//   0  did the thing
//   1  tried and failed (database down, guild not found)
//   2  the command line was wrong
//
// It talks to Postgres and Redis directly and never to Discord — see CliHost for why that
// matters and what it costs.

string verb = args.FirstOrDefault() ?? "help";
CliArgs cli = new(args);

if (verb is "help" or "-h" or "--help")
{
    Help.Print(args.Skip(1).FirstOrDefault());
    return 0;
}

if (verb is "version" or "--version")
{
    Console.WriteLine(Help.Version);
    return 0;
}

try
{
    switch (verb)
    {
        case "stats":
            return await StatsCommand.RunAsync(cli);

        case "chat":
            return await ChatCommand.RunAsync(cli);

        case "set":
            return await SetCommand.RunAsync(cli);

        case "get":
            return await GetCommand.RunAsync(cli);

        case "guilds":
            return await GuildsCommand.RunAsync(cli);

        default:
            Console.Error.WriteLine($"sonarr: '{verb}' isn't a command. Try `sonarr help`.");
            return 2;
    }
}
catch (CliError ex)
{
    // The message is already written for a person to read; the exit code is the machine's copy.
    Console.Error.WriteLine($"sonarr: {ex.Message}");
    return ex.ExitCode;
}
catch (Exception ex)
{
    // Same rule as the Migrator: connection strings carry passwords, so report the exception
    // and never what we were handed.
    //
    // The two innermost messages, not the outermost. EF wraps a refused connection twice, and the
    // outer wrapper — "An exception has been raised that is likely due to a transient failure" —
    // names neither the host nor the problem. One level down is Npgsql's "Failed to connect to
    // 127.0.0.1:5433" (a host and port, which are not secrets) and below that the socket's own
    // "the target machine actively refused it". Together those two say what failed and what it was
    // doing; the wrappers above them say nothing a person can act on.
    List<string> chain = [];
    for (Exception? link = ex; link is not null; link = link.InnerException)
    {
        chain.Add(link.Message);
    }

    IEnumerable<string> deepest = chain
        .TakeLast(2)
        .Distinct(StringComparer.Ordinal);

    Console.Error.WriteLine($"sonarr {verb} failed: {string.Join(" — ", deepest)}");
    return 1;
}
finally
{
    CliHost.Dispose();
}
