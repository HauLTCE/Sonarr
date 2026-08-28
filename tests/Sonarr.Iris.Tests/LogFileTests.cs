using Sonarr.Iris.Logging;

namespace Sonarr.Iris.Tests;

/// <summary>
/// <see cref="LogFile"/> against real temp files — the reader only earns its keep if it survives
/// the shapes Serilog actually writes: ISO timestamps, spaced level tokens, exception stacks.
/// </summary>
public sealed class LogFileTests
{
    /// <summary>The file template's shape: ISO timestamp, <c>[INF]</c>-style level, context, message.</summary>
    private static string Line(string level, string message)
        => $"2026-08-28T12:34:56.7890123+02:00 [{level}] Sonarr.Test {message}";

    [Fact]
    public void Tail_ReturnsTheLastNLines()
    {
        using TempDir dir = new();
        string path = dir.File("sonarr-20260828.log",
            string.Join('\n', Enumerable.Range(1, 20).Select(i => Line("INF", $"line {i}"))) + '\n');

        IReadOnlyList<string> tail = LogFile.Tail(new FileInfo(path), 5);

        Assert.Equal(Enumerable.Range(16, 5).Select(i => Line("INF", $"line {i}")), tail);
    }

    [Fact]
    public void Tail_CountLargerThanTheFile_ReturnsEverything()
    {
        using TempDir dir = new();
        string[] lines = Enumerable.Range(1, 3).Select(i => Line("INF", $"line {i}")).ToArray();
        string path = dir.File("sonarr-20260828.log", string.Join('\n', lines) + '\n');

        Assert.Equal(lines, LogFile.Tail(new FileInfo(path), 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Tail_NonPositiveCount_ReturnsNothing(int count)
    {
        using TempDir dir = new();
        string path = dir.File("sonarr-20260828.log", Line("INF", "hello") + '\n');

        Assert.Empty(LogFile.Tail(new FileInfo(path), count));
    }

    [Fact]
    public void Tail_EmptyFile_ReturnsNothing()
    {
        using TempDir dir = new();
        string path = dir.File("sonarr-20260828.log", string.Empty);

        Assert.Empty(LogFile.Tail(new FileInfo(path), 10));
    }

    [Fact]
    public void Tail_FileSpanningManyChunks_ReturnsWholeLinesOnly()
    {
        // ~60 KB: the 8 KB backwards reader crosses several chunks, where the cut lands mid-line
        // and the partial first line must be dropped rather than printed mangled.
        using TempDir dir = new();
        string[] lines = Enumerable.Range(1, 500)
            .Select(i => Line("INF", $"line {i} with some padding to widen the record")).ToArray();
        string path = dir.File("sonarr-20260828.log", string.Join('\n', lines) + '\n');

        IReadOnlyList<string> tail = LogFile.Tail(new FileInfo(path), 25);

        Assert.Equal(lines.TakeLast(25), tail);
    }

    [Fact]
    public void FilterElevated_KeepsWarningsAndWorseWithTheirStacks()
    {
        string[] input =
        [
            Line("INF", "all quiet"),
            Line("WRN", "something wobbled"),
            "System.InvalidOperationException: wobble",
            "   at Sonarr.Test.Wobble()",
            Line("INF", "still quiet"),
            Line("ERR", "something broke"),
        ];

        IReadOnlyList<string> kept = LogFile.FilterElevated(input);

        Assert.Equal(
        [
            Line("WRN", "something wobbled"),
            "System.InvalidOperationException: wobble",
            "   at Sonarr.Test.Wobble()",
            Line("ERR", "something broke"),
        ], kept);
    }

    [Fact]
    public void FilterElevated_ContinuationBeforeAnyRecord_IsDropped()
    {
        // A stack line above the first record belongs to nothing; keeping it would misattribute it
        // to whatever record prints first.
        IReadOnlyList<string> kept = LogFile.FilterElevated(
        [
            "   at Sonarr.Test.Orphan()",
            Line("WRN", "a warning"),
        ]);

        Assert.Equal([Line("WRN", "a warning")], kept);
    }

    [Theory]
    [InlineData(" [WRN] ", true)]
    [InlineData(" [ERR] ", true)]
    [InlineData(" [FTL] ", true)]
    [InlineData(" [INF] ", false)]
    [InlineData(" [DBG] ", false)]
    public void IsElevated_MatchesTheLevelTokenOnly(string marker, bool expected)
    {
        // The spaces are the guard: a message body that merely contains "WRN" is not a warning.
        Assert.Equal(expected, LogFile.IsElevated($"2026-08-28T00:00:00{marker}Source text"));
    }

    [Fact]
    public void IsRecordStart_KnowsTimestampsFromStacks()
    {
        Assert.True(LogFile.IsRecordStart(Line("INF", "a record")));
        Assert.False(LogFile.IsRecordStart("   at Sonarr.Test.Continuation()"));
        Assert.False(LogFile.IsRecordStart("System.Exception: not a timestamp"));
    }

    [Fact]
    public void Newest_PicksTheLatestDayByName_NotByMtime()
    {
        using TempDir dir = new();
        string older = dir.File("sonarr-20260101.log", Line("INF", "old"));
        string newer = dir.File("sonarr-20260102.log", Line("INF", "new"));

        // A copied tree keeps names but not mtimes; the reader must agree, so pin the trap:
        // the older-named file gets the newer mtime and must still lose.
        File.SetLastWriteTime(older, DateTime.Now);
        File.SetLastWriteTime(newer, DateTime.Now.AddDays(-2));

        Assert.Equal(new FileInfo(newer).Name, LogFile.Newest(new DirectoryInfo(dir.Path))!.Name);
    }

    [Fact]
    public void Newest_IgnoresForeignFiles()
    {
        using TempDir dir = new();
        dir.File("notes.txt", "not a log");
        dir.File("sonarr-latest.log", Line("INF", "not dated"));
        string dated = dir.File("sonarr-20260102.log", Line("INF", "dated"));

        Assert.Equal(new FileInfo(dated).Name, LogFile.Newest(new DirectoryInfo(dir.Path))!.Name);
    }

    [Fact]
    public void Newest_EmptyOrMissingDirectory_ReturnsNull()
    {
        using TempDir dir = new();
        Assert.Null(LogFile.Newest(new DirectoryInfo(dir.Path)));
        Assert.Null(LogFile.Newest(new DirectoryInfo(System.IO.Path.Combine(dir.Path, "nope"))));
    }
}
