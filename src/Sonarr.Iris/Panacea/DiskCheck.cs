namespace Sonarr.Iris.Panacea;

/// <summary>
/// Free space on the volume the bot writes to. This disk has filled twice (docs/11 — ~2.9 GB of
/// Docker build cache per bot rebuild), and every symptom of a full disk arrives disguised as
/// something else: a pg_dump that truncates, a log sink that stops, a migration that half
/// applies. So it is examined before those, and the remedy is the one deletion that is safe
/// without asking — Docker's reclaimable build cache and dangling images, which are by
/// construction rebuildable.
/// </summary>
/// <remarks>
/// Nothing of the bot's own is ever deleted here, and nothing a running service needs. Log
/// retention belongs to Serilog and backup retention to the nightly job, both of which already
/// prune on their own schedules; a doctor that deleted a dump to free space would be destroying
/// the thing the operator came to protect. The report says what is left and lets a person
/// decide.
/// </remarks>
internal sealed class DiskCheck : IDoctorCheck
{
    /// <summary>Below this, the next dump or migration is at risk. A dump is ~50-100 MB here.</summary>
    private const long LowBytes = 1024L * 1024 * 1024;

    /// <summary>Below this it is already failing things silently.</summary>
    private const long CriticalBytes = 256L * 1024 * 1024;

    public string Name => "disk";

    public Task<Diagnosis> ExamineAsync(CancellationToken ct)
    {
        DriveInfo drive;
        try
        {
            drive = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/");
            if (!drive.IsReady)
            {
                return Task.FromResult(Diagnosis.Blocked("the volume is not ready to report"));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Diagnosis.Blocked($"cannot read the volume — {Doctor.OneLine(ex.Message)}"));
        }

        long free = drive.AvailableFreeSpace;
        string summary = $"{Gb(free)} free of {Gb(drive.TotalSize)} on {drive.Name}";

        if (free < CriticalBytes)
        {
            return Task.FromResult(Diagnosis.Broken(
                summary,
                "this is already breaking writes: a dump truncates, a migration half-applies, the "
                + "log sink stops. The doctor reclaims Docker build cache; anything beyond that is "
                + "`du -xh --max-depth=2 / | sort -h | tail` and a decision"));
        }

        return Task.FromResult(free < LowBytes
            ? Diagnosis.Broken(
                summary,
                "under a gigabyte, which is less than a few dumps — the doctor reclaims Docker "
                + "build cache, and docs/11 lists what else grows here")
            : Diagnosis.Ok(summary));
    }

    public async Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
    {
        // `builder prune` and `image prune`, deliberately not `system prune`: that one also
        // removes every stopped container, which is precisely what PostgresCheck and RedisCheck
        // start back up — freeing disk by deleting the stack the next check means to repair.
        // Neither of these two can: build cache and dangling images are rebuildable by
        // construction, and `image prune` without -a leaves every image a container references.
        List<string> steps = ["reclaiming Docker build cache"];
        steps.AddRange(await Shell.RunAsync(
            "docker", ["builder", "prune", "-f"], TimeSpan.FromMinutes(5), ct));

        steps.Add("reclaiming dangling images");
        steps.AddRange(await Shell.RunAsync(
            "docker", ["image", "prune", "-f"], TimeSpan.FromMinutes(5), ct));

        return steps;
    }

    private static string Gb(long bytes) => bytes >= 1L << 30
        ? FormattableString.Invariant($"{bytes / (double)(1L << 30):0.0} GB")
        : FormattableString.Invariant($"{bytes / (double)(1L << 20):0} MB");
}
