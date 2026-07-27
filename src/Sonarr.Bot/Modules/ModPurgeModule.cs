using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Discord;
using Sonarr.Bot.Discord.Moderation;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Moderation;

namespace Sonarr.Bot.Modules;

/// <summary>
/// The channel-level moderation commands: <c>/purge</c> and <c>/slowmode</c>. Both file a case.
/// </summary>
/// <remarks>
/// <c>/purge preview:true</c> is a dry run and deletes nothing. This module never calls
/// Discord's delete API itself — it hands a deleter delegate to
/// <see cref="IModerationService.PurgeAsync"/>, which refuses to invoke it for a preview. The
/// contract therefore holds even if this module is wrong.
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Moderation)]
public sealed class ModPurgeModule(IModerationService moderation) : ModModuleBase(moderation)
{
    [SlashCommand("purge", "Bulk-delete recent messages. Use preview to see what would go first.")]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task PurgeAsync(
        [Summary("count", "How many messages to scan back over (1-100)")]
        [MinValue(PurgeFilter.MinCount)][MaxValue(PurgeFilter.MaxCount)] int count,
        [Summary("from", "Only this user's messages")] SocketGuildUser? from = null,
        [Summary("contains", "Only messages containing this text")] string? contains = null,
        [Summary("bots", "Only bot messages")] bool bots = false,
        [Summary("preview", "Dry run: report what would be deleted, delete nothing")] bool preview = false)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (InputGuards.InRange(count, PurgeFilter.MinCount, PurgeFilter.MaxCount, "count") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        if (contains is not null && InputGuards.Length(contains, 200, "contains") is { } textProblem)
        {
            await RespondInvalidAsync(textProblem);
            return;
        }

        if (Context.Channel is not ITextChannel channel)
        {
            await RespondInvalidAsync("I can only purge a text channel.");
            return;
        }

        // Purges can take a moment: two REST round trips plus a bulk delete.
        await DeferAsync(ephemeral: true);

        PurgeFilter filter = new(count, from?.Id, contains, bots, preview);

        IEnumerable<IMessage> history = await channel
            .GetMessagesAsync(PurgeFilter.MaxCount, options: new RequestOptions { CancelToken = default })
            .FlattenAsync();

        List<PurgeCandidate> candidates = [.. history.Select(Candidate)];

        PurgePreview result = await Moderation.PurgeAsync(
            guild.Id,
            channel.Id,
            Context.User.Id,
            filter,
            candidates,
            (ids, ct) => DeleteAsync(channel, ids, ct));

        await FollowupAsync(Describe(result, filter), ephemeral: true);
    }

    [SlashCommand("slowmode", "Set this channel's slowmode, or turn it off.")]
    [DefaultMemberPermissions(GuildPermission.ManageChannels)]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task SlowmodeAsync(
        [Summary("duration", "Per-message delay — 10s, 2m, up to 6h. Say `off` to clear it.")] string duration)
    {
        if (await RequireGuildAsync() is not { } guild)
        {
            return;
        }

        if (Context.Channel is not ITextChannel channel)
        {
            await RespondInvalidAsync("Slowmode is a text-channel thing.");
            return;
        }

        var off = duration.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)
            || duration.Trim() == "0";

        var seconds = 0;
        if (!off)
        {
            if (!DurationText.TryParse(duration, out TimeSpan span, out var problem))
            {
                await RespondInvalidAsync(problem);
                return;
            }

            seconds = (int)span.TotalSeconds;
            if (InputGuards.InRange(seconds, 1, ModerationLimits.MaxSlowmodeSeconds, "slowmode")
                is { } rangeProblem)
            {
                await RespondInvalidAsync(rangeProblem);
                return;
            }
        }

        await channel.ModifyAsync(c => c.SlowModeInterval = seconds);

        CaseRecord record = await Moderation.RecordSlowmodeAsync(
            guild.Id, channel.Id, Context.User.Id, seconds);

        await RespondPublicAsync(off
            ? $"**Case #{record.CaseId}** — slowmode off in {channel.Mention}."
            : $"**Case #{record.CaseId}** — slowmode {DurationText.Describe(TimeSpan.FromSeconds(seconds))} " +
              $"in {channel.Mention}.");
    }

    private static PurgeCandidate Candidate(IMessage message) => new(
        message.Id,
        message.Author.Id,
        message.Author.IsBot,
        message.Content ?? string.Empty,
        message.Timestamp,
        message is IUserMessage { IsPinned: true });

    private static async Task<int> DeleteAsync(
        ITextChannel channel, IReadOnlyList<ulong> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        if (ids.Count == 1)
        {
            // Discord rejects a single-message bulk delete.
            await channel.DeleteMessageAsync(ids[0], new RequestOptions { CancelToken = ct });
            return 1;
        }

        await channel.DeleteMessagesAsync(ids, new RequestOptions { CancelToken = ct });
        return ids.Count;
    }

    private static string Describe(PurgePreview result, PurgeFilter filter)
    {
        var skipped = Skipped(result);

        if (result.WasDryRun)
        {
            return result.Matched == 0
                ? $"Dry run: nothing matches {Filters(filter)}. Nothing deleted."
                : $"Dry run: **{result.Matched}** message(s) match {Filters(filter)}.{skipped}\n" +
                  "Nothing was deleted. Run it again without `preview` to go ahead.";
        }

        return result.Deleted == 0
            ? $"Nothing matched {Filters(filter)} — nothing deleted.{skipped}"
            : $"Deleted **{result.Deleted}** message(s) matching {Filters(filter)}.{skipped}";
    }

    private static string Skipped(PurgePreview result)
    {
        List<string> notes = [];
        if (result.Pinned > 0)
        {
            notes.Add($"{result.Pinned} pinned");
        }

        if (result.TooOld > 0)
        {
            notes.Add($"{result.TooOld} older than 14 days");
        }

        return notes.Count == 0 ? string.Empty : $" Skipped {string.Join(" and ", notes)}.";
    }

    /// <summary>Names the filters that were used — never the message text that matched.</summary>
    private static string Filters(PurgeFilter filter)
    {
        List<string> parts = [$"the last {filter.Count}"];
        if (filter.FromUserId is { } userId)
        {
            parts.Add($"from <@{userId}>");
        }

        if (!string.IsNullOrWhiteSpace(filter.Contains))
        {
            parts.Add("containing your search text");
        }

        if (filter.BotsOnly)
        {
            parts.Add("from bots");
        }

        return string.Join(" ", parts);
    }
}
