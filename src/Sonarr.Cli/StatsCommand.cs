using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Sonarr.Domain.Abstractions;

namespace Sonarr.Cli;

/// <summary>
/// <c>sonarr stats</c> — the durable analytics for one guild.
/// </summary>
/// <remarks>
/// Backed by <see cref="IStatsRepository.GetStatsAsync"/>, whose own doc said "No caller on the
/// current surface" — it was written for the panel that is gone. This is its caller now, and it is a
/// better fit than the bot's <c>SonarrMetrics</c> counters: those live in the bot's memory, reset on
/// restart, and there is no IPC to reach them from a second process.
/// </remarks>
internal static class StatsCommand
{
    public static async Task<int> RunAsync(CliArgs cli)
    {
        ulong guildId = cli.Guild();
        if (guildId == 0)
        {
            throw CliError.Usage(
                "stats needs a server: pass --guild ID, or set SONARR_GUILD in .env. "
                + "(`sonarr guilds` lists them.)");
        }

        int days = cli.Int("days", 30);
        if (days < 1)
        {
            throw CliError.Usage("--days wants at least 1.");
        }

        using IServiceScope scope = CliHost.Scope();
        IGuildRepository guilds = scope.ServiceProvider.GetRequiredService<IGuildRepository>();
        Domain.Entities.Core.Guild guild = await guilds.GetAsync((long)guildId)
            ?? throw new CliError($"no server {guildId} in the database. `sonarr guilds` lists them.", 2);

        GuildStats stats = await scope.ServiceProvider
            .GetRequiredService<IStatsRepository>()
            .GetStatsAsync((long)guildId, days);

        Console.WriteLine($"{guild.Name} ({guildId}) — last {Output.Count(stats.Days, "day", "days")}, times in {Output.Zone}");

        Commands(stats);
        Activity(stats);
        Growth(stats);
        return 0;
    }

    private static void Commands(GuildStats stats)
    {
        Output.Heading("Commands");
        if (stats.Commands.Count == 0)
        {
            Console.WriteLine("  (none in this window)");
            return;
        }

        long busiest = stats.Commands.Max(c => c.Count);
        Output.Rows(stats.Commands.Select(c => (
            c.Command,
            $"{c.Count.ToString("N0", CultureInfo.InvariantCulture),7}  {Output.Bar(c.Count, busiest)}")));
    }

    /// <remarks>
    /// Rolled up to a day per line. The stored series is hourly, and a 30-day window of hours is
    /// 720 rows — more than anyone reads in a terminal, and the shape of a month is what the
    /// question "how busy has it been" is actually asking.
    /// </remarks>
    private static void Activity(GuildStats stats)
    {
        Output.Heading("Activity");
        if (stats.Activity.Count == 0)
        {
            Console.WriteLine("  (no samples in this window)");
            return;
        }

        var byDay = stats.Activity
            .GroupBy(a => DateOnly.FromDateTime(a.HourBucket.ToLocalTime().DateTime))
            .Select(g => new
            {
                Day = g.Key,
                Messages = g.Sum(a => (long)a.Messages),
                // Presence is a sample rather than a total, so the day's figure is its peak —
                // summing 24 hourly headcounts would report a number of people who do not exist.
                PeakVoice = g.Max(a => a.VoiceUsers),
                PeakOnline = g.Max(a => a.OnlineEstimate),
            })
            .OrderBy(d => d.Day)
            .ToList();

        long busiest = byDay.Max(d => d.Messages);
        Output.Rows(byDay.Select(d => (
            Output.LocalDate(d.Day),
            $"{d.Messages.ToString("N0", CultureInfo.InvariantCulture),6} msg  "
            + $"peak {d.PeakOnline,3} online / {d.PeakVoice,2} voice  {Output.Bar(d.Messages, busiest)}")));

        Console.WriteLine();
        Console.WriteLine($"  {Output.Count(stats.Activity.Count, "hour sampled", "hours sampled")}, "
            + $"{stats.Activity.Sum(a => (long)a.Messages).ToString("N0", CultureInfo.InvariantCulture)} messages total.");
    }

    private static void Growth(GuildStats stats)
    {
        Output.Heading("New members");
        if (stats.Growth.Count == 0)
        {
            Console.WriteLine("  (nobody new in this window)");
            return;
        }

        long busiest = stats.Growth.Max(g => (long)g.Joined);
        Output.Rows(stats.Growth.Select(g => (
            Output.LocalDate(g.Day),
            $"{g.Joined,4}  {Output.Bar(g.Joined, busiest)}")));

        Console.WriteLine();
        Console.WriteLine($"  {Output.Count(stats.Growth.Sum(g => g.Joined), "member", "members")} joined.");
    }
}
