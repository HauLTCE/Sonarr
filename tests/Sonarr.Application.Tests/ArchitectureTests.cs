using System.Reflection;
using System.Xml.Linq;

namespace Sonarr.Application.Tests;

/// <summary>
/// Enforces the dependency direction from docs/02-architecture.md:
/// <c>Bot → Application → Domain ← Infrastructure</c>, and the determinism boundary
/// that keeps <c>Sonarr.Elaine</c> pure.
/// </summary>
// ponytail: assembly-reference checks only — cheap and catches the mistakes that
// actually happen (a stray `using Discord;` in a service). If we ever need per-type
// rules, NetArchTest is the upgrade.
public sealed class ArchitectureTests
{
    // Load by name: the compiler drops references a project doesn't actually use yet,
    // so walking Application's reference list would silently skip these while the
    // projects are still thin.
    private static Assembly Application => Assembly.Load("Sonarr.Application");
    private static Assembly Domain => Assembly.Load("Sonarr.Domain");
    private static Assembly Elaine => Assembly.Load("Sonarr.Elaine");

    private static string[] ReferenceNames(Assembly assembly)
        => [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? "")];

    [Theory]
    [InlineData("Sonarr.Infrastructure")]
    [InlineData("Sonarr.Bot")]
    public void Application_does_not_reference_outward(string forbidden)
        => Assert.DoesNotContain(forbidden, ReferenceNames(Application));

    [Theory]
    [InlineData("Discord.Net.Core")]
    [InlineData("Discord.Net.WebSocket")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("StackExchange.Redis")]
    public void Application_holds_no_transport_or_storage_dependency(string forbidden)
        => Assert.DoesNotContain(forbidden, ReferenceNames(Application));

    [Theory]
    [InlineData("Sonarr.Application")]
    [InlineData("Sonarr.Infrastructure")]
    [InlineData("Sonarr.Bot")]
    [InlineData("Sonarr.Elaine")]
    public void Domain_depends_on_nothing_of_ours(string forbidden)
        => Assert.DoesNotContain(forbidden, ReferenceNames(Domain));

    /// <summary>
    /// docs/10-elaine-engine.md: the engine is pure logic — no Discord, no DB, no HTTP,
    /// and no ambient clock. Time and RNG are injected.
    /// </summary>
    [Theory]
    [InlineData("Sonarr.Infrastructure")]
    [InlineData("Sonarr.Bot")]
    [InlineData("Discord.Net.Core")]
    [InlineData("Discord.Net.WebSocket")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("StackExchange.Redis")]
    [InlineData("Microsoft.AspNetCore.Http.Abstractions")]
    public void Elaine_stays_pure(string forbidden)
        => Assert.DoesNotContain(forbidden, ReferenceNames(Elaine));

    /// <summary>
    /// The <c>sonarr</c> CLI has no gateway, and that is a design decision rather than a gap
    /// nobody got round to.
    /// </summary>
    /// <remarks>
    /// A second process that connects to Discord needs the bot token, and a shell tool that reads
    /// the token is one accident away from being a second bot session — which Discord answers by
    /// disconnecting the first. <c>CliHost</c> pays for that with <c>OfflineGuildDirectory</c>,
    /// documented in <c>sonarr help set</c>: the CLI cannot tell a channel id from a role id.
    /// <para>
    /// The obvious "fix" for that limitation is to add Discord.Net to the CLI, which is exactly what
    /// must not happen — so it fails here, with the reason attached. Read from the csproj rather than
    /// by assembly reference because the test project does not reference the CLI, and giving it one
    /// would put the CLI's dependencies on the test assembly's own probing path.
    /// </para>
    /// <para>
    /// The <c>Include</c> attributes, not the file's text: the csproj explains in a comment why it
    /// takes no Discord.Net, so a text scan matches the comment and fails a project that is correct.
    /// A guard that cannot tell the defect from the thing documenting its own absence gets deleted
    /// the first time it cries wolf.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_cli_takes_no_gateway_dependency()
    {
        FileInfo project = new(Path.Combine(RepoRoot().FullName, "src", "Sonarr.Iris", "Sonarr.Iris.csproj"));

        // Asserted, not skipped: a moved or renamed project must fail loudly rather than turn this
        // into a test that passes by finding nothing.
        Assert.True(project.Exists, $"{project.FullName} is not there — did the CLI project move?");

        List<string> gateway = [.. XDocument
            .Load(project.FullName)
            .Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .Where(include => include.Contains("Discord", StringComparison.OrdinalIgnoreCase))];

        Assert.True(
            gateway.Count == 0,
            "The CLI runs without a gateway on purpose (CliHost). Reaching for Discord.Net to close "
                + "the IGuildDirectory gap makes a shell tool hold the bot token; use /config set for "
                + $"the channel and role keys instead. Found: {string.Join(", ", gateway)}");
    }

    private static DirectoryInfo RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sonarr.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!;
    }
}
