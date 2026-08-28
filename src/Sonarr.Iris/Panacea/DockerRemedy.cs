using System.Diagnostics;

namespace Sonarr.Iris.Panacea;

/// <summary>
/// The one remedy two checks share: a dependency that is down because its container was stopped.
/// The doctor finds the stopped container that matches the service and starts it — nothing is
/// installed, created or configured, because a container that merely stopped is the only state
/// that can be fixed without knowing how the box was built.
/// </summary>
internal static class DockerRemedy
{
    /// <summary>How long <c>docker ps</c> / <c>docker start</c> get before they are a failure.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Try to start a stopped container for <paramref name="service"> and wait for
    /// <paramref name="probe"/> to agree it is back. Returns the steps attempted, each one a
    /// sentence fit for the report.
    /// </summary>
    public static async Task<List<string>> TryStartAsync(
        string service, Func<Task<bool>> probe, CancellationToken ct)
    {
        List<string> steps = [];

        (int found, string psOutput, string? whichError) = await RunAsync(
            ["ps", "-a", "--filter", "status=exited", "--format", "{{.ID}}|{{.Names}}|{{.Image}}"], ct);
        if (whichError is not null)
        {
            steps.Add($"looked for a stopped {service} container — docker is not usable here ({whichError})");
            return steps;
        }

        (string id, string name) = FindContainer(psOutput, service);
        if (id.Length == 0)
        {
            steps.Add($"looked for a stopped {service} container — there is none to start");
            return steps;
        }

        (int started, _, string? startError) = await RunAsync(["start", id], ct);
        if (startError is not null || started != 0)
        {
            steps.Add($"tried to start container '{name}' — {(startError ?? $"docker start exited {started}")}");
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
        foreach (string line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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

    /// <summary>
    /// One docker command. The exit code, stdout, and null-or-a-reason when docker itself could
    /// not run (not on PATH, refused to start). Command output is capped: a container listing
    /// that is somehow megabytes long has no business in a report.
    /// </summary>
    private static async Task<(int Exit, string Stdout, string? Error)> RunAsync(
        string[] arguments, CancellationToken ct)
    {
        ProcessStartInfo start = new("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("docker did not start");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, "", "not found or not runnable");
        }

        using (process)
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

            using CancellationTokenSource timeout = new(CommandTimeout);
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Kill(process);
                return (-1, "", $"timed out after {CommandTimeout.TotalSeconds:F0} s");
            }

            await Task.WhenAll(stdout, stderr);
            string output = stdout.Result.Length > 4000 ? stdout.Result[..4000] : stdout.Result;
            string error = stderr.Result.Trim();
            return (process.ExitCode, output, process.ExitCode == 0 ? null : Doctor.OneLine(error));
        }
    }

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

    private static void Kill(Process process)
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
