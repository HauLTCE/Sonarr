using Sonarr.Domain.Jobs;

namespace Sonarr.Iris.Tests;

/// <summary>
/// <see cref="BackupsCommand.Inspect"/> against a fabricated backup tree: the same file names
/// <see cref="BackupRetention.RelativePath"/> writes in production, graded for a chosen today so
/// nothing in here depends on the wall clock.
/// </summary>
public sealed class BackupsInspectTests
{
    private static readonly DateOnly Today = new(2026, 8, 28);

    [Fact]
    public void Inspect_HealthyTree_PassesClean()
    {
        using TempDir dir = new();
        WriteDump(dir, Today.AddDays(-1));
        WriteDump(dir, Today.AddDays(-2));
        WriteDump(dir, Today.AddDays(-3), prefix: BackupRetention.ConfigPrefix,
            extension: BackupRetention.ConfigExtension);
        dir.File("notes.txt", "somebody's memo");

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        Assert.Equal(2, report.Dumps.Count);
        Assert.Single(report.Configs);
        Assert.Equal(1, report.Strays);
        Assert.Empty(report.Expired);
        Assert.Null(report.NewestDumpProblem);
        Assert.False(report.Stale);
    }

    [Fact]
    public void Inspect_TruncatedNewestDump_NamesTheProblem()
    {
        using TempDir dir = new();
        WriteDump(dir, Today.AddDays(-2));
        WriteDump(dir, Today.AddDays(-1), content: new byte[10]);

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        Assert.NotNull(report.NewestDumpProblem);
        Assert.Contains("truncated", report.NewestDumpProblem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_WrongMagicHeader_NamesTheProblem()
    {
        using TempDir dir = new();
        byte[] notADump = new byte[2048];
        "HELLO world, but no archive"u8.CopyTo(notADump);
        WriteDump(dir, Today.AddDays(-1), content: notADump);

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        Assert.NotNull(report.NewestDumpProblem);
        Assert.Contains("PGDMP", report.NewestDumpProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_NewestDumpMoreThanTwoDaysOld_IsStale()
    {
        using TempDir dir = new();
        WriteDump(dir, Today.AddDays(-3));

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        Assert.True(report.Stale);
        Assert.Null(report.NewestDumpProblem);
    }

    [Fact]
    public void Inspect_SecondFileOfAnOldMonth_IsDueForPruning()
    {
        // Past the 30-day window, each month keeps its earliest surviving file — so in a month
        // with two, exactly one is expired. The first-of-month survives as the monthly.
        using TempDir dir = new();
        WriteDump(dir, new DateOnly(2026, 7, 1));
        WriteDump(dir, new DateOnly(2026, 7, 2));
        WriteDump(dir, Today.AddDays(-1));

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        BackupFile expired = Assert.Single(report.Expired);
        Assert.Equal(new DateOnly(2026, 7, 2), expired.Day);
    }

    [Fact]
    public void Inspect_EmptyTree_ReportsNothingWithoutCrashing()
    {
        using TempDir dir = new();

        BackupReport report = BackupsCommand.Inspect(dir.Path, Today);

        Assert.Empty(report.Dumps);
        Assert.Empty(report.Configs);
        Assert.Equal(0, report.Strays);
        Assert.Null(report.NewestDumpProblem);
        Assert.False(report.Stale);
    }

    [Fact]
    public void FindDir_ExistingFlag_Wins()
    {
        using TempDir dir = new();

        Assert.Equal(dir.Path, BackupsCommand.FindDir(dir.Path));
    }

    [Fact]
    public void FindDir_MissingFlag_IsAUsageError()
    {
        using TempDir dir = new();

        CliError error = Assert.Throws<CliError>(() => BackupsCommand.FindDir(dir.Path + "/nope"));
        Assert.Equal(2, error.ExitCode);
    }

    /// <summary>
    /// Writes a backup file where <see cref="BackupRetention.RelativePath"/> says it lives, so
    /// the tree under test and the tree the nightly job writes cannot drift in shape.
    /// </summary>
    private static void WriteDump(
        TempDir dir,
        DateOnly day,
        string prefix = BackupRetention.DumpPrefix,
        string extension = BackupRetention.DumpExtension,
        byte[]? content = null)
    {
        content ??= ValidDump();
        string relative = BackupRetention.RelativePath(prefix, day, extension);
        string full = Path.Combine(dir.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }

    /// <summary>2 KB starting with the pg_dump custom-format magic: restorable-shaped.</summary>
    private static byte[] ValidDump()
    {
        byte[] data = new byte[2048];
        BackupVerification.CustomFormatMagic.CopyTo(data, 0);
        return data;
    }
}
