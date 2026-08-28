using Sonarr.Iris.Panacea;

namespace Sonarr.Iris.Tests;

/// <summary>
/// The Panacea doctor against fake checks: the runner's contract — when it treats, when it
/// does not, and what a case file records — plus the two helpers the live checks lean on
/// (<see cref="Doctor.OneLine"/> secret scrubbing, <see cref="DockerRemedy.FindContainer"/>
/// parsing). The checks themselves need live Postgres/Redis/docker and are exercised by hand.
/// </summary>
public sealed class PanaceaTests
{
    private class FakeCheck : IDoctorCheck
    {
        private readonly Queue<Diagnosis> _examinations = new();

        public required string Name { get; init; }

        public List<string> Steps { get; init; } = [];

        public int TreatCalls { get; private set; }

        /// <summary>What each successive examination returns; the last one repeats.</summary>
        public FakeCheck Examinations(params Diagnosis[] diagnoses)
        {
            foreach (Diagnosis diagnosis in diagnoses)
            {
                _examinations.Enqueue(diagnosis);
            }

            return this;
        }

        public Task<Diagnosis> ExamineAsync(CancellationToken ct)
            => Task.FromResult(_examinations.Count > 1 ? _examinations.Dequeue() : _examinations.Peek());

        public virtual Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
        {
            TreatCalls++;
            return Task.FromResult<IReadOnlyList<string>>([.. Steps]);
        }
    }

    [Fact]
    public async Task HealthyCheck_IsNeverTreated()
    {
        FakeCheck check = new FakeCheck { Name = "fine" }
            .Examinations(Diagnosis.Ok("all good"));

        List<CaseFile> files = await Doctor.RunAsync([check], heal: true);

        CaseFile file = Assert.Single(files);
        Assert.True(file.Final.Healthy);
        Assert.False(file.Healed);
        Assert.Equal(0, check.TreatCalls);
    }

    [Fact]
    public async Task BrokenThenFixed_IsHealedWithItsSteps()
    {
        FakeCheck check = new FakeCheck { Name = "schema", Steps = ["applied 2 migrations"] }
            .Examinations(
                Diagnosis.Broken("2 migrations pending", "re-run to apply"),
                Diagnosis.Ok("up to date"));

        List<CaseFile> files = await Doctor.RunAsync([check], heal: true);

        CaseFile file = Assert.Single(files);
        Assert.True(file.Healed);
        Assert.False(file.StillBroken);
        Assert.Equal(1, check.TreatCalls);
        Assert.Equal("applied 2 migrations", Assert.Single(file.Attempted));
    }

    [Fact]
    public async Task BrokenAndUnfixable_KeepsTheAdvice()
    {
        FakeCheck check = new FakeCheck { Name = "persona" }
            .Examinations(Diagnosis.Broken("3 errors", "run `sonarr persona`"));

        List<CaseFile> files = await Doctor.RunAsync([check], heal: true);

        CaseFile file = Assert.Single(files);
        Assert.True(file.StillBroken);
        Assert.False(file.Healed);
        Assert.Equal("run `sonarr persona`", file.Advice);
    }

    [Fact]
    public async Task CheckOnlyMode_NeverTreats()
    {
        FakeCheck check = new FakeCheck { Name = "postgres" }
            .Examinations(Diagnosis.Broken("refused", "start it"));

        List<CaseFile> files = await Doctor.RunAsync([check], heal: false);

        CaseFile file = Assert.Single(files);
        Assert.True(file.StillBroken);
        Assert.Equal(0, check.TreatCalls);
        Assert.Empty(file.Attempted);
    }

    [Fact]
    public async Task ThrowingExamination_IsABrokenVerdict_NotACrash()
    {
        FakeThrowingCheck check = new();

        List<CaseFile> files = await Doctor.RunAsync([check], heal: true);

        CaseFile file = Assert.Single(files);
        Assert.True(file.StillBroken);
        Assert.Contains("boom", file.Final.Detail);
    }

    [Fact]
    public async Task ThrowingTreatment_IsAStep_NotACrash()
    {
        FakeCheck check = new ThrowingTreatCheck { Name = "redis" }
            .Examinations(Diagnosis.Broken("down"));

        List<CaseFile> files = await Doctor.RunAsync([check], heal: true);

        CaseFile file = Assert.Single(files);
        Assert.True(file.StillBroken);
        Assert.Contains("treatment failed", Assert.Single(file.Attempted));
    }

    [Fact]
    public void OneLine_BlanksPasswordFragments()
    {
        string line = Doctor.OneLine(
            "Failed to connect: Host=127.0.0.1;Port=5432;Password=hunter2;Database=sonarr");

        Assert.DoesNotContain("hunter2", line);
        Assert.Contains("password=***", line);
    }

    [Fact]
    public void OneLine_TakesOnlyTheFirstLineAndCapsIt()
    {
        string line = Doctor.OneLine(new string('x', 300) + "\nsecond line");

        Assert.DoesNotContain("second", line);
        Assert.True(line.Length <= 120);
        Assert.EndsWith("…", line);
    }

    [Theory]
    [InlineData("abc123|sonarr-dev-postgres-1|pgvector/pgvector:pg17", "postgres", "abc123")]
    [InlineData("def456|cache|redis:7.4-alpine", "redis", "def456")]
    public void FindContainer_MatchesNameOrImage(string row, string service, string expectedId)
    {
        (string id, _) = DockerRemedy.FindContainer(row, service);

        Assert.Equal(expectedId, id);
    }

    [Fact]
    public void FindContainer_IgnoresForeignServicesAndMalformedRows()
    {
        string output = string.Join('\n',
            "not-a-row",
            "x1|lavalink|ghcr.io/lavalink/lavalink",
            "x2|sonarr-dev-redis-1|redis:7.4-alpine");

        (string id, string name) = DockerRemedy.FindContainer(output, "postgres");

        Assert.Equal("", id);

        (id, name) = DockerRemedy.FindContainer(output, "redis");
        Assert.Equal("x2", id);
        Assert.Equal("sonarr-dev-redis-1", name);
    }

    // -------------------------------------------------- the shared external-command wrapper

    [Fact]
    public async Task Command_ThatCannotStart_IsNotStarted_NotAFailedExit()
    {
        CommandResult result = await Command.RunAsync(
            "a-binary-that-is-not-installed-anywhere", [], TimeSpan.FromSeconds(5), default);

        Assert.False(result.Started);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Shell_SaysSoWhenTheBinaryIsMissing()
    {
        List<string> steps = await Shell.RunAsync(
            "a-binary-that-is-not-installed-anywhere", [], TimeSpan.FromSeconds(5), default);

        Assert.Contains("could not run", Assert.Single(steps));
    }

    [Fact]
    public async Task Command_CapturesStdoutFromSomethingThatRuns()
    {
        // dotnet is on PATH wherever these tests run — it is what is running them.
        CommandResult result = await Command.RunAsync(
            "dotnet", ["--version"], TimeSpan.FromSeconds(60), default);

        Assert.True(result.Started);
        Assert.Equal(0, result.Exit);
        Assert.NotEmpty(result.Stdout.Trim());
    }

    [Fact]
    public async Task Shell_ReportsNothingButDoneOnSuccess()
    {
        List<string> steps = await Shell.RunAsync(
            "dotnet", ["--version"], TimeSpan.FromSeconds(60), default);

        Assert.Equal("done", Assert.Single(steps));
    }

    // -------------------------------------------------------- the unit check, off the server

    [Fact]
    public async Task ServiceCheck_OffTheServer_IsBlockedRatherThanRed()
    {
        // The bot is a systemd unit on the server and nothing at all on a dev box, so "no unit"
        // must not read as "the bot is down" — a red row here would be a false alarm on every
        // developer machine. On Linux the same call reaches systemctl and grades a real unit,
        // so the assertion is the one thing true either way: it answers, and never throws.
        Diagnosis diagnosis = await new ServiceCheck("sonarr", "bot").ExamineAsync(default);

        if (!OperatingSystem.IsLinux())
        {
            Assert.Equal(Verdict.Blocked, diagnosis.Kind);
            Assert.Contains("systemd", diagnosis.Detail);
            return;
        }

        // A unit that is absent, stopped or failed is broken with advice; only "active" is green.
        Assert.True(
            diagnosis.Kind != Verdict.Broken || diagnosis.Advice is not null,
            "a broken unit must carry advice");
    }

    private sealed class FakeThrowingCheck : IDoctorCheck
    {
        public string Name => "exploding";

        public Task<Diagnosis> ExamineAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    private sealed class ThrowingTreatCheck : FakeCheck
    {
        public override Task<IReadOnlyList<string>> TreatAsync(Diagnosis ailment, CancellationToken ct)
            => throw new InvalidOperationException("the remedy exploded");
    }
}
