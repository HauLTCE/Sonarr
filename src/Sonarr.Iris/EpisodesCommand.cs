using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Entities.Chat;
using Sonarr.Domain.Entities.Core;

namespace Sonarr.Iris;

/// <summary>
/// <c>sonarr episodes</c> — what she has been saying lately, across the whole guild: her authored
/// lines with who she said them to, newest last.
/// </summary>
/// <remarks>
/// Episodes are the one chat record that is safe to read out: the store only ever holds her own
/// replies, never the user's message that prompted them, so this command prints personal data
/// the privacy rule already approved. Attribution resolves through the member cache and falls
/// back to the raw id for anyone the bot never saw speak (rare — an episode implies a turn).
/// </remarks>
internal static class EpisodesCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ArgumentNullException.ThrowIfNull(cli);

        ulong guildId = cli.Guild();
        if (guildId == 0)
        {
            throw CliError.Usage(
                "episodes needs a server: pass --guild ID, or set SONARR_GUILD in .env. "
                + "(`sonarr guilds` lists them.)");
        }

        int days = cli.Int("days", 7);
        if (days < 1)
        {
            throw CliError.Usage("--days wants at least 1.");
        }

        int limit = cli.Int("limit", 50);
        if (limit < 1)
        {
            throw CliError.Usage("--limit wants at least 1.");
        }

        using IServiceScope scope = CliHost.Scope();
        IServiceProvider services = scope.ServiceProvider;

        IEpisodeRepository episodes = services.GetRequiredService<IEpisodeRepository>();
        IMemberRepository members = services.GetRequiredService<IMemberRepository>();

        DateTimeOffset since = DateTimeOffset.UtcNow.AddDays(-days);
        IReadOnlyList<Episode> rows = await episodes.GetRecentAsync((long)guildId, since, limit);

        Console.WriteLine(
            $"Guild {guildId} — last {Output.Count(days, "day", "days")}, times in {Output.Zone}");
        Output.Heading($"{Output.Count(rows.Count, "line", "lines")} of hers");
        if (rows.Count == 0)
        {
            Console.WriteLine($"  (nothing in the last {Output.Count(days, "day", "days")})");
            return 0;
        }

        Dictionary<long, string> names = [];
        foreach (Episode episode in rows)
        {
            string who = await WhoAsync(members, names, (long)guildId, episode.UserId);
            Console.WriteLine(
                $"  {Output.Local(episode.HappenedAt)}  t{episode.Turn,-5} {who,-20}  {episode.Quote}");
        }

        return 0;
    }

    /// <summary>
    /// Display name for attribution, cached per user — a busy hour repeats the same few people,
    /// and one repository round-trip per distinct user keeps it cheap.
    /// </summary>
    private static async Task<string> WhoAsync(
        IMemberRepository members, Dictionary<long, string> cache, long guildId, long userId)
    {
        if (cache.TryGetValue(userId, out string? cached))
        {
            return cached;
        }

        Member? member = await members.GetAsync(guildId, userId);
        string name = member is null
            ? userId.ToString(CultureInfo.InvariantCulture)
            : member.DisplayName;
        cache[userId] = name;
        return name;
    }
}
