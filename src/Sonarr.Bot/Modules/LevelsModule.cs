using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Levels;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/level</c>, <c>/rank</c>, <c>/leaderboard</c>, <c>/compare</c>, <c>/userstats</c>
/// (docs/07-commands.md#levels). Thin translator: guard the input, call
/// <see cref="ILevelService"/>, format.
/// </summary>
[RequireFeature(FeatureNames.Levels)]
public sealed class LevelsModule(ILevelService levels) : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("level", "Your level and XP, or someone else's.")]
    public async Task LevelAsync([Summary("user", "Whose level to look up")] IUser? user = null)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IUser target = user ?? Context.User;
        LevelCard card = await levels.GetCardAsync(guildId, target.Id);

        await RespondPublicAsync(
            $"**{Name(target)}** — level {card.Level}, {Number(card.Xp)} XP"
            + (card.Rank > 0 ? $" (#{card.Rank})" : " (no XP yet)"));
    }

    [SlashCommand("rank", "Progress card: level, bar, streak.")]
    public async Task RankAsync([Summary("user", "Whose card to show")] IUser? user = null)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IUser target = user ?? Context.User;
        LevelCard card = await levels.GetCardAsync(guildId, target.Id);

        var embed = new EmbedBuilder()
            .WithAuthor(Name(target), target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl())
            .WithColor(new Color(0x5865F2))
            .WithDescription(
                $"`{LevelBar.Render(card.Fraction)}` {LevelBar.Percent(card.Fraction)}\n"
                + $"{Number(card.XpIntoLevel)} / {Number(card.XpForLevel)} XP this level")
            .AddField("Level", card.Level, inline: true)
            .AddField("Rank", card.Rank > 0 ? $"#{card.Rank}" : "unranked", inline: true)
            .AddField("Total XP", Number(card.Xp), inline: true)
            // The card's streak is already derived against today (StreakRules.CurrentDays), so a
            // broken one shows as none rather than yesterday's number.
            .AddField("Streak", Streak(card.StreakDays), inline: true)
            .AddField("Voice", LevelBar.Duration(card.VoiceSeconds), inline: true)
            .AddField("To next level", $"{Number(card.XpToNextLevel)} XP", inline: true);

        await RespondAsync(embed: embed.Build());
    }

    [SlashCommand("leaderboard", "Who's ahead. Pick a season for the frozen standings.")]
    public async Task LeaderboardAsync(
        [Summary("season", "A past season, or leave empty for all time")]
        [Autocomplete(typeof(SeasonAutocompleteHandler))] string? season = null,
        [Summary("page", "Page number")] int page = 1)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        if (InputGuards.InRange(page, 1, 1000, "page") is { } pageProblem)
        {
            await RespondInvalidAsync(pageProblem);
            return;
        }

        long? seasonId = null;
        if (!string.IsNullOrWhiteSpace(season))
        {
            if (!long.TryParse(season, CultureInfo.InvariantCulture, out var parsed))
            {
                await RespondInvalidAsync("Pick a season from the list — that isn't one of mine.");
                return;
            }

            seasonId = parsed;
        }

        LeaderboardPage board = await levels.GetLeaderboardAsync(guildId, seasonId, page);

        if (board.Entries.Count == 0)
        {
            await RespondPublicAsync($"Nothing on the board for **{board.Title}** yet.");
            return;
        }

        var lines = new StringBuilder();
        foreach (LeaderboardEntry entry in board.Entries)
        {
            var medal = entry.Rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"`#{entry.Rank}`" };
            var level = entry.Level > 0 ? $" · level {entry.Level}" : string.Empty;
            lines.AppendLine($"{medal} <@{entry.UserId}> — {Number(entry.Xp)} XP{level}");
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Leaderboard — {board.Title}")
            .WithColor(new Color(0xFEE75C))
            .WithDescription(lines.ToString())
            .WithFooter($"Page {board.Page} of {board.PageCount}")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("compare", "Side by side with someone else.")]
    public async Task CompareAsync(
        [Summary("user", "Who to compare against")] IUser user,
        [Summary("with", "Compare this user instead of yourself")] IUser? with = null)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IUser left = with ?? Context.User;
        if (left.Id == user.Id)
        {
            await RespondInvalidAsync("Comparing someone with themselves is a draw. Pick two people.");
            return;
        }

        LevelComparison comparison = await levels.CompareAsync(guildId, left.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle($"{Name(left)} vs {Name(user)}")
            .WithColor(new Color(0xEB459E))
            .AddField(Name(left), Column(comparison.Left), inline: true)
            .AddField(Name(user), Column(comparison.Right), inline: true)
            .WithFooter(comparison.XpGap == 0
                ? "Dead even."
                : $"{Number(comparison.XpGap)} XP apart — {Name(comparison.LeaderId == left.Id ? left : user)} is ahead.")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("userstats", "Messages, join date, activity.")]
    public async Task UserStatsAsync([Summary("user", "Whose stats to show")] IUser? user = null)
    {
        if (await RequireGuildAsync() is not { } guildId)
        {
            return;
        }

        IUser target = user ?? Context.User;
        MemberStats stats = await levels.GetStatsAsync(guildId, target.Id);

        var embed = new EmbedBuilder()
            .WithAuthor(Name(target), target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl())
            .WithColor(new Color(0x57F287))
            .AddField("Messages", Number(stats.MessageCount), inline: true)
            .AddField("Level", stats.Level.Level, inline: true)
            .AddField("Total XP", Number(stats.Level.Xp), inline: true)
            .AddField("Streak", $"{stats.Level.StreakDays} day(s)", inline: true)
            .AddField("Voice", LevelBar.Duration(stats.Level.VoiceSeconds), inline: true)
            .AddField("First seen", Stamp(stats.FirstSeenAt), inline: true)
            .AddField("Last active", Stamp(stats.LastActiveAt), inline: true);

        await RespondAsync(embed: embed.Build());
    }

    private static string Column(LevelCard card)
        => $"Level **{card.Level}**\n{Number(card.Xp)} XP\n"
           + (card.Rank > 0 ? $"Rank #{card.Rank}\n" : "Unranked\n")
           + $"`{LevelBar.Render(card.Fraction)}`\nStreak {Streak(card.StreakDays)}";

    /// <summary>
    /// Zero is "none" rather than "0 days" — a broken streak reads as a state, not as a count.
    /// </summary>
    public static string Streak(int days) => days switch
    {
        <= 0 => "none",
        1 => "1 day",
        _ => $"{days} days",
    };

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Stamp(DateTimeOffset? when)
        => when is { } value ? TimestampTag.FromDateTimeOffset(value, TimestampTagStyles.Relative).ToString() : "unknown";

    private static string Name(IUser user) => (user as IGuildUser)?.DisplayName ?? user.Username;

    /// <summary>Levels are per server; a DM has nothing to report. Answers the user and returns null.</summary>
    private async Task<ulong?> RequireGuildAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild.Id;
        }

        await RespondInvalidAsync("XP is per server — run this in one.");
        return null;
    }
}

/// <summary>Autocomplete over the guild's seasons (docs/07-commands.md#design-rules).</summary>
public sealed class SeasonAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;
        var levels = services.GetRequiredService<ILevelService>();

        IReadOnlyList<SeasonSummary> seasons = await levels.GetSeasonsAsync(context.Guild.Id);

        IEnumerable<AutocompleteResult> matches = seasons
            .Select(s => new
            {
                Label = $"Season {s.SeasonId} ({s.StartsAt:yyyy-MM-dd} → {s.EndsAt:yyyy-MM-dd}, {s.Status})",
                Value = s.SeasonId.ToString(CultureInfo.InvariantCulture),
            })
            .Where(s => s.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(s => new AutocompleteResult(s.Label, s.Value));

        return AutocompletionResult.FromSuccess(matches);
    }
}
