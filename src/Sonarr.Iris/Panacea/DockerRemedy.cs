namespace Sonarr.Iris.Panacea;

/// <summary>
/// The one remedy two checks share: a dependency that is down because its container was stopped.
/// The doctor finds the stopped container that matches the service and starts it — nothing is
/// installed, created or configured, because a container that merely stopped is the only state
/// that can be fixed without knowing how the box was built.
/// </summary>
internal static class DockerRemedy
{
    /// <summary>How long a docker call gets before it counts as unusable.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Try to start a stopped container for <paramref name="service"/> and wait for
    /// <paramref name="probe"/> to agree it is back. Returns the steps attempted, each one a
    /// sentence fit for the report.
    /// </summary>
    public static async Task<List<string>> TryStartAsync(
        string service, Func<Task<bool>> probe, CancellationToken ct)
    {
        List<string> steps = [];

        CommandResult listing = await Command.RunAsync(
            "docker",
            ["ps", "-a", "--filter", "status=exited", "--format", "{{.ID}}|{{.Names}}|{{.Image}}"],
            CommandTimeout,
            ct);

        if (!listing.Started || listing.TimedOut || listing.Exit != 0)
        {
            steps.Add($"looked for a stopped {service} container — docker is not usable here "
                + $"({Unusable(listing)})");
            return steps;
        }

        (string id, string name) = FindContainer(listing.Stdout, service);
        if (id.Length == 0)
        {
            steps.Add($"looked for a stopped {service} container — there is none to start");
            return steps;
        }

        CommandResult started = await Command.RunAsync("docker", ["start", id], CommandTimeout, ct);
        if (!started.Started || started.TimedOut || started.Exit != 0)
        {
            steps.Add($"tried to start container '{name}' — {Unusable(started)}");
            return steps;
        }

        steps.Add($"started container '{name}'");

        // A container that just started takes a moment to accept connections; give it ten seconds
        // of probing so the re-examination is not racing the boot.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            if (await ProbeSafely(probe))
            {
                steps.Add($"{service} is answering again");
                return steps;
            }
        }

        steps.Add($"started '{name}', but {service} is still not answering");
        return steps;
    }

    /// <summary>
    /// The first exited container whose name or image names the service. Matching is a
    /// substring on purpose: compose stacks name containers after the project, the service, or
    /// both, and a match that asks for the exact convention is a match that misses the box.
    /// </summary>
    internal static (string Id, string Name) FindContainer(string psOutput, string service)
    {
        foreach (string line in psOutput.Split(
            '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split('|', 3);
            if (parts.Length < 3)
            {
                continue;
            }

            if (parts[1].Contains(service, StringComparison.OrdinalIgnoreCase)
                || parts[2].Contains(service, StringComparison.OrdinalIgnoreCase))
            {
                return (parts[0], parts[1]);
            }
        }

        return ("", "");
    }

    /// <summary>Why a docker call is no use, in the words the report prints.</summary>
    private static string Unusable(CommandResult result) => result switch
    {
        { Started: false } => result.Error ?? "not runnable",
        { TimedOut: true } => $"timed out after {CommandTimeout.TotalSeconds:F0} s",
        _ => Doctor.OneLine(result.Stderr.Trim().Length > 0
            ? result.Stderr
            : $"docker exited {result.Exit}"),
    };

    private static async Task<bool> ProbeSafely(Func<Task<bool>> probe)
    {
        try
        {
            return await probe();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
