using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sonarr.Infrastructure.Persistence;
using Sonarr.Infrastructure.Persistence.Repositories.Stats;

namespace Sonarr.Application.Tests.Stats;

/// <summary>
/// Guards the one bug class a fake repository can never catch: a LINQ query that compiles and then
/// throws at first execution because the provider cannot translate it. /admin/stats returned
/// "Could not load that" on every call for exactly this reason, and 1115 green tests missed it.
/// </summary>
public sealed class QueryTranslationTests
{
    /// <summary>
    /// The specific shape that broke: a <c>GroupBy</c> projected straight into a named type's
    /// constructor. EF rewrites the aggregate inside it as <c>g.AsQueryable().Sum(...)</c> — a
    /// correlated subquery over the group rather than an aggregate — and throws. Projecting into an
    /// anonymous type first and constructing the record after translates fine, which is what every
    /// working query in the codebase does.
    /// </summary>
    /// <remarks>
    /// Source-level rather than executed, because executing only reaches the <em>first</em> query
    /// in a method: a repository whose opening query is fine dies on the connection and never
    /// translates the rest. That is not a hypothetical — the music sites below sit behind an
    /// earlier query and passed a live-execution test while still broken. Reading the source
    /// reaches every site regardless of position, with no database.
    /// </remarks>
    [Fact]
    public void No_GroupBy_projects_into_a_named_constructor()
    {
        // `new {` is the safe form; `new SomeType(` is the one that compiles and then throws.
        Regex namedCtor = new(@"\.Select\(\w+ => new [A-Z]\w+\(", RegexOptions.Compiled);

        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                int groupBy = lines[i].IndexOf(".GroupBy(", StringComparison.Ordinal);
                if (groupBy < 0)
                {
                    continue;
                }

                // The statement, from the GroupBy to the semicolon that ends the fluent chain.
                List<(int Line, string Text)> block = [];
                for (int j = i; j < Math.Min(i + 20, lines.Length); j++)
                {
                    block.Add((j, j == i ? lines[j][groupBy..] : lines[j]));
                    if (lines[j].Contains(';', StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                // No await means in-memory LINQ over a materialised list, which translates nothing
                // and is allowed to project however it likes (StatsRepository's growth query).
                if (!block.Exists(b => b.Text.Contains("Async(", StringComparison.Ordinal)))
                {
                    continue;
                }

                // Only the first Select runs over the group. Ones after it see already-aggregated
                // rows, so constructing a record there is the fix, not the bug.
                (int Line, string Text) select =
                    block.Find(b => b.Text.Contains(".Select(", StringComparison.Ordinal));

                if (select.Text is not null && namedCtor.IsMatch(select.Text))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{select.Line + 1} {select.Text.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A GroupBy projects an aggregate straight into a constructor; project into an anonymous "
            + "type and build the record after:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The query that actually broke the page, translated by the real Npgsql provider. Port 1 is
    /// unbound on purpose: EF translates before it opens a connection, so an untranslatable query
    /// fails with "could not be translated" while a translatable one gets as far as the socket.
    /// The assertion is that the failure is about the network, not about the query.
    /// </summary>
    [Fact]
    public async Task GuildStats_first_query_translates()
    {
        await using SonarrDbContext db = new(new DbContextOptionsBuilder<SonarrDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=sonarr;Username=none;Password=none;Timeout=1",
                npgsql => npgsql.UseVector())
            .Options);

        var repo = new StatsRepository(db);
        Exception? thrown = null;

        try
        {
            await repo.GetStatsAsync(1, 30);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.NotNull(thrown);
        Assert.DoesNotContain("could not be translated", thrown.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Walks up from the test binary to the repo's <c>src</c> directory.</summary>
    private static string SourceRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(candidate, "Sonarr.Infrastructure")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the src/ directory");
    }
}
