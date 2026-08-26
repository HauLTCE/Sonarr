using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Core;
using Sonarr.Infrastructure.Persistence;

namespace Sonarr.Cli;

/// <summary>
/// <c>sonarr guilds</c> — what is in <c>core.guild</c>, so the other commands have an id to be
/// given.
/// </summary>
/// <remarks>
/// Counts come from a grouped query against the context rather than from a repository method,
/// because no repository has one and adding <c>CountMembersAsync</c> to
/// <see cref="IMemberRepository"/> would put a method on the bot's interface that only the CLI
/// calls. The Migrator uses <see cref="SonarrDbContext"/> directly for the same reason.
/// </remarks>
internal static class GuildsCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        using IServiceScope scope = CliHost.Scope();
        IReadOnlyList<Guild> guilds = await scope.ServiceProvider
            .GetRequiredService<IGuildRepository>()
            .GetAllAsync();

        if (guilds.Count == 0)
        {
            Console.WriteLine("No servers in the database yet — the bot writes a row when it joins one.");
            return 0;
        }

        SonarrDbContext db = scope.ServiceProvider.GetRequiredService<SonarrDbContext>();
        Dictionary<long, int> members = await db.Members
            .AsNoTracking()
            .GroupBy(m => m.GuildId)
            .Select(g => new { GuildId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.GuildId, g => g.Count);

        Console.WriteLine($"{Output.Count(guilds.Count, "server", "servers")}, times in {Output.Zone}");
        Output.Rows(guilds
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                g.GuildId.ToString(CultureInfo.InvariantCulture),
                $"{g.Name}  —  {(members.TryGetValue(g.GuildId, out int n) ? n : 0)} known members, "
                + $"joined {Output.Local(g.JoinedAt)}")));

        return 0;
    }
}
