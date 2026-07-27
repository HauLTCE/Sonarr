using System.Reflection;

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
}
