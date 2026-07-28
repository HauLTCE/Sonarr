using System.Text;
using Sonarr.Bot.Discord.Jobs;
using Sonarr.Domain.Jobs;

namespace Sonarr.Application.Tests.Jobs;

/// <summary>
/// The naming, verification and retention rules of docs/11. All pure — the parts of the backup that
/// can silently delete the wrong thing live here rather than in the runner, precisely so they can be
/// tested without a database or a disk.
/// </summary>
public class BackupRetentionTests
{
    private static readonly DateOnly Today = new(2026, 7, 28);

    private static BackupFile Dump(string day)
        => new($"/backups/{day[..4]}/{day[5..7]}/sonarr-{day}.dump", BackupRetention.DumpPrefix,
            DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));

    private static BackupFile Config(string day)
        => new($"/backups/{day[..4]}/{day[5..7]}/config-{day}.tar.gz", BackupRetention.ConfigPrefix,
            DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_path_is_foldered_by_year_and_month()
        => Assert.Equal(
            "2026/07/sonarr-2026-07-28.dump",
            BackupRetention.RelativePath(BackupRetention.DumpPrefix, Today, BackupRetention.DumpExtension));

    /// <summary>docs/11's example filename, character for character apart from the deliberate `.gz`.</summary>
    [Theory]
    [InlineData("/backups/2026/07/sonarr-2026-07-28.dump", "sonarr", "2026-07-28")]
    [InlineData("/backups/2026/01/config-2026-01-05.tar.gz", "config", "2026-01-05")]
    [InlineData("C:\\backups\\2026\\07\\sonarr-2026-07-28.dump", "sonarr", "2026-07-28")]
    public void A_name_round_trips_through_parse(string path, string prefix, string day)
    {
        BackupFile parsed = Assert.IsType<BackupFile>(BackupRetention.Parse(path));

        Assert.Equal(prefix, parsed.Prefix);
        Assert.Equal(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), parsed.Day);
        Assert.Equal(path, parsed.Path);
    }

    /// <summary>
    /// Anything unrecognised parses to null, and <see cref="BackupRetention.Expired"/> only ever
    /// names what it parsed — so a stray file in the backup tree is left alone rather than deleted by
    /// a rule written without it in mind.
    /// </summary>
    [Theory]
    [InlineData("/backups/README")]                     // no extension at all
    [InlineData("/backups/2026/07/sonarr-latest.dump")] // no date
    [InlineData("/backups/2026/07/sonarr-2026-7-8.dump")] // not zero-padded
    [InlineData("/backups/2026/07/sonarr-2026-13-01.dump")] // month 13
    [InlineData("/backups/2026/07/sonarr-2026-07-28.dump.partial")] // a dump still being written
    public void An_unrecognised_name_is_never_a_backup(string path)
        => Assert.Null(BackupRetention.Parse(path));

    /// <summary>
    /// A dump mid-write carries `.partial`, so the first dot lands before the date and it cannot be
    /// parsed — which is what stops a prune from deleting the dump the runner is still writing.
    /// </summary>
    [Fact]
    public void A_partial_dump_is_invisible_to_the_prune()
    {
        BackupFile[] found = [.. new[]
        {
            "/backups/2026/07/sonarr-2026-07-28.dump.partial",
            "/backups/2026/07/sonarr-2026-07-28.dump",
        }.Select(BackupRetention.Parse).OfType<BackupFile>()];

        Assert.Equal("sonarr-2026-07-28.dump", Path.GetFileName(Assert.Single(found).Path));
    }

    [Fact]
    public void Everything_inside_thirty_days_is_kept()
    {
        BackupFile[] dailies = [.. Enumerable.Range(0, 31).Select(d => Dump(Today.AddDays(-d).ToString("yyyy-MM-dd")))];

        Assert.Empty(BackupRetention.Expired(dailies, Today));
    }

    /// <summary>
    /// Past the window the rule changes: only the month's earliest survives. May is wholly outside
    /// the 30 days, so this is the monthly rule on its own — see the next test for the boundary.
    /// </summary>
    [Fact]
    public void Past_the_window_only_the_months_earliest_survives()
    {
        BackupFile[] may = [.. Enumerable.Range(1, 31).Select(d => Dump($"2026-05-{d:D2}"))];

        IReadOnlyList<BackupFile> expired = BackupRetention.Expired(may, Today);

        Assert.Equal(30, expired.Count);
        Assert.DoesNotContain(expired, f => f.Day.Day == 1);
    }

    /// <summary>
    /// A month straddling the boundary must not keep two files. Computing "earliest" over the whole
    /// month rather than over its expired half is what prevents that.
    /// </summary>
    [Fact]
    public void A_month_straddling_the_boundary_keeps_one_file()
    {
        // 2026-06-28 is exactly 30 days before Today, so late June is inside the window and
        // early June is not.
        BackupFile[] june = [.. Enumerable.Range(1, 30).Select(d => Dump($"2026-06-{d:D2}"))];

        BackupFile[] kept = [.. june.Except(BackupRetention.Expired(june, Today))];

        Assert.Equal(new DateOnly(2026, 6, 1), kept.Min(f => f.Day));
        // The survivors are the 1st plus everything the daily window still covers — no second
        // "oldest of the month" sneaking through.
        Assert.Equal(1 + 3, kept.Length);
    }

    [Fact]
    public void Monthlies_expire_after_twelve_months()
    {
        BackupFile[] monthlies =
        [
            Dump("2025-07-01"), // 12 months old — the last one kept
            Dump("2025-06-01"), // 13 — gone
            Dump("2024-12-01"),
        ];

        IReadOnlyList<BackupFile> expired = BackupRetention.Expired(monthlies, Today);

        Assert.Equal(
            [new DateOnly(2024, 12, 1), new DateOnly(2025, 6, 1)],
            expired.Select(f => f.Day).OrderBy(d => d));
    }

    /// <summary>
    /// Retention is per set. A weekly archive rarely lands on the 1st, so the literal
    /// "first-of-month" reading would delete almost every config backup — hence "earliest of the
    /// month", judged within each prefix.
    /// </summary>
    [Fact]
    public void The_two_sets_are_pruned_independently()
    {
        BackupFile[] june =
        [
            Dump("2026-06-01"), Dump("2026-06-02"),
            Config("2026-06-01"), Config("2026-06-08"), Config("2026-06-15"),
        ];

        BackupFile[] kept = [.. june.Except(BackupRetention.Expired(june, Today))];

        Assert.Equal(
            [(BackupRetention.ConfigPrefix, 1), (BackupRetention.DumpPrefix, 1)],
            kept.Select(f => (f.Prefix, f.Day.Day)).OrderBy(x => x.Prefix));
    }

    [Fact]
    public void A_dump_with_the_pg_dump_header_passes()
        => Assert.Null(BackupVerification.Check(4096, BackupVerification.CustomFormatMagic));

    /// <summary>The failure that actually happens: pg_dump killed mid-write.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(BackupVerification.MinimumBytes - 1)]
    public void A_truncated_dump_is_rejected(long bytes)
        => Assert.Contains("truncated", BackupVerification.Check(bytes, BackupVerification.CustomFormatMagic));

    /// <summary>
    /// Also catches a format change: gzip and plain SQL both fail the header check, and both would
    /// break the `pg_restore` the drill in docs/11 documents.
    /// </summary>
    [Theory]
    [InlineData("-- PostgreSQL database dump")]
    [InlineData("\u001f\u008b\b\0")]
    public void A_dump_in_the_wrong_format_is_rejected(string header)
        => Assert.Contains("PGDMP", BackupVerification.Check(8192, Encoding.Latin1.GetBytes(header)));

    /// <summary>A dump too short to even hold the magic must not read past what was read.</summary>
    [Fact]
    public void A_dump_shorter_than_the_magic_is_rejected()
        => Assert.NotNull(BackupVerification.Check(4096, "PG"u8));

    [Fact]
    public void The_dump_runs_before_the_retention_sweep()
        => Assert.True(BackupRunner.RunAt < RetentionPruner.RunAt);

    [Fact]
    public void The_next_run_is_the_next_three_thirty()
        => Assert.Equal(
            TimeSpan.FromMinutes(90),
            DailySchedule.UntilNextRun(new DateTimeOffset(2026, 7, 28, 2, 0, 0, TimeSpan.Zero), BackupRunner.RunAt));

    /// <summary>Otherwise a dump finishing inside the same minute would immediately dump again.</summary>
    [Fact]
    public void The_run_time_itself_waits_a_whole_day()
        => Assert.Equal(
            TimeSpan.FromDays(1),
            DailySchedule.UntilNextRun(new DateTimeOffset(2026, 7, 28, 3, 30, 0, TimeSpan.Zero), BackupRunner.RunAt));
}
