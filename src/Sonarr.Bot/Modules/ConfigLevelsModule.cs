using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/config-levels</c> — the XP surface of the settings (docs/07-commands.md#levels): role
/// rewards, where level-ups are announced, and the event multiplier. Same
/// <see cref="IGuildConfigService"/> as <see cref="ConfigModule"/>, so validation and cache
/// invalidation are shared; this only narrows the keys so an admin is not scrolling past music.
/// </summary>
/// <remarks>
/// A sibling group name rather than a <c>/config levels</c> subgroup: Discord.Net registers one
/// module per top-level group, and <see cref="ConfigModule"/> already owns <c>config</c>.
/// <see cref="ConfigModerationModule"/> made the same call.
/// </remarks>
[Group("config-levels", "XP settings: role rewards, level-up channel, multiplier.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
// DefaultMemberPermissions is a default a server admin can override; this is the enforcement.
[RequireUserPermission(GuildPermission.ManageGuild)]
[RequireContext(ContextType.Guild)]
public sealed class ConfigLevelsModule(IGuildConfigService config, ILevelService levels)
    : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Matches <c>LevelService.MaxRewardLevel</c>; the service is still the authority.</summary>
    private const int MaxRewardLevel = 500;

    /// <summary>Matches the bound <see cref="LevelsConfigKeys.ParseWeights"/> enforces.</summary>
    private const int MaxChannelWeight = 500;

    /// <summary>What an unweighted channel already earns, so storing it is redundant.</summary>
    private const int DefaultChannelWeight = 100;

    [SlashCommand("show", "Show the XP settings in effect here.")]
    public async Task ShowAsync()
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        LevelsPolicy policy = await levels.GetPolicyAsync(guild.Id);
        IReadOnlyList<RoleReward> rewards = await levels.GetRewardsAsync(guild.Id);

        StringBuilder text = new("**XP settings**\n");
        text.Append("Level-up announcements: ")
            .Append(policy.LevelupChannelId is { } channelId ? $"<#{channelId}>" : "the channel they levelled in")
            .Append('\n');
        text.Append("Also DM them: ").Append(policy.AnnounceByDm ? "yes" : "no").Append('\n');
        text.Append("XP multiplier: ").Append(policy.XpMultiplier.ToString(CultureInfo.InvariantCulture))
            .Append("x\n");
        text.Append("Inactivity decay: ").Append(policy.DecayEnabled ? "on" : "off").Append('\n');

        text.Append("Channel weights: ");
        if (policy.ChannelWeights.Count == 0)
        {
            text.Append("all channels count normally\n");
        }
        else
        {
            text.AppendLine(string.Join(", ", policy.ChannelWeights
                .OrderBy(w => w.Key)
                .Select(w => $"<#{w.Key}> {w.Value.ToString(CultureInfo.InvariantCulture)}%")));
        }

        text.Append("\n**Role rewards**\n");
        if (rewards.Count == 0)
        {
            text.Append("None yet. `/config-levels reward-add` to set one up.");
        }
        else
        {
            foreach (RoleReward reward in rewards)
            {
                text.AppendLine($"Level {reward.Level.ToString(CultureInfo.InvariantCulture)} → <@&{reward.RoleId}>");
            }
        }

        await RespondPersonalAsync(text.ToString());
    }

    [SlashCommand("reward-add", "Give a role automatically at a level.")]
    public async Task RewardAddAsync(
        [Summary("level", "The level that earns the role")] int level,
        [Summary("role", "The role to grant")] IRole role)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        ArgumentNullException.ThrowIfNull(role);

        if (InputGuards.InRange(level, LevelCurve.FirstLevel, MaxRewardLevel, "level") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        if (InputGuards.BelongsToGuild(role.Guild.Id, guild.Id, "role") is { } wrongGuild)
        {
            await RespondInvalidAsync(wrongGuild);
            return;
        }

        if (role.IsManaged)
        {
            await RespondInvalidAsync($"<@&{role.Id}> belongs to an integration — Discord won't let me hand it out.");
            return;
        }

        // Hierarchy is Discord.Net's own "highest role position, or int.MaxValue if owner", so
        // there is nothing to compute here. Null guard: an uncached guild has no CurrentUser yet,
        // and a missing self is not a reason to refuse a reward the service will validate anyway.
        if (guild.CurrentUser is { } me && role.Position >= me.Hierarchy)
        {
            await RespondInvalidAsync(
                $"<@&{role.Id}> sits above my own role, so I can't grant it. Move my role higher and try again.");
            return;
        }

        ConfigWriteResult result = await levels.SetRewardAsync(guild.Id, level, role.Id);
        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("reward-remove", "Stop granting a role at a level.")]
    public async Task RewardRemoveAsync([Summary("level", "The level to clear")] int level)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        if (InputGuards.InRange(level, LevelCurve.FirstLevel, MaxRewardLevel, "level") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        ConfigWriteResult result = await levels.RemoveRewardAsync(guild.Id, level);
        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("channel", "Send every level-up to one channel instead of where it happened.")]
    public async Task ChannelAsync(
        [Summary("channel", "Where to announce level-ups. Leave empty to go back to in-place.")]
        ITextChannel? channel = null)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        if (channel is null)
        {
            ConfigWriteResult cleared = await config.ClearAsync(
                guild.Id, ConfigKeys.LevelupChannel, Context.User.Id);
            await RespondPersonalAsync(cleared.Message);
            return;
        }

        if (InputGuards.BelongsToGuild(channel.GuildId, guild.Id, "channel") is { } wrongGuild)
        {
            await RespondInvalidAsync(wrongGuild);
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id,
            ConfigKeys.LevelupChannel,
            channel.Id.ToString(CultureInfo.InvariantCulture),
            Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("dm", "Also DM people when they level up.")]
    public async Task DmAsync([Summary("state", "On or off")] bool state)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id, ConfigKeys.LevelupDm, state ? "true" : "false", Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("multiplier", "Double XP weekend and friends. 1 is normal.")]
    public async Task MultiplierAsync([Summary("times", "1 to 5")] int times)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        if (InputGuards.InRange(times, 1, 5, "multiplier") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id,
            ConfigKeys.XpMultiplier,
            times.ToString(CultureInfo.InvariantCulture),
            Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("decay", "Let inactive members drift back down the leaderboard.")]
    public async Task DecayAsync([Summary("state", "On or off")] bool state)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id, LevelsConfigKeys.XpDecay, state ? "true" : "false", Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }

    [SlashCommand("channel-weight", "Change how much XP one channel is worth. 0 mutes it.")]
    public async Task ChannelWeightAsync(
        [Summary("channel", "The channel to reweight")] ITextChannel channel,
        [Summary("percent", "0 to 500. 100 is normal.")] int percent)
    {
        if (Context.Guild is not { } guild)
        {
            await RespondInvalidAsync("Config lives per server — run this in one.");
            return;
        }

        ArgumentNullException.ThrowIfNull(channel);

        if (InputGuards.BelongsToGuild(channel.GuildId, guild.Id, "channel") is { } wrongGuild)
        {
            await RespondInvalidAsync(wrongGuild);
            return;
        }

        if (InputGuards.InRange(percent, 0, MaxChannelWeight, "percent") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        // One key holds every channel's weight (the catalog is a closed set), so a single
        // channel's edit is read-modify-write. Same-guild single-admin surface; a lost update
        // here costs one re-run of the command.
        LevelsPolicy policy = await levels.GetPolicyAsync(guild.Id);
        Dictionary<ulong, int> weights = new(policy.ChannelWeights);
        if (percent == DefaultChannelWeight)
        {
            // Back to normal is an absence, not a stored 100 — keeps the string from growing
            // one pair per channel that was only ever toggled and reset.
            weights.Remove(channel.Id);
        }
        else
        {
            weights[channel.Id] = percent;
        }

        if (weights.Count == 0)
        {
            ConfigWriteResult cleared = await config.ClearAsync(
                guild.Id, LevelsConfigKeys.XpChannelWeights, Context.User.Id);
            await RespondPersonalAsync(cleared.Message);
            return;
        }

        ConfigWriteResult result = await config.SetAsync(
            guild.Id,
            LevelsConfigKeys.XpChannelWeights,
            string.Join(',', weights.OrderBy(w => w.Key).Select(w =>
                $"{w.Key}:{w.Value.ToString(CultureInfo.InvariantCulture)}")),
            Context.User.Id);

        await RespondPersonalAsync(result.Message);
    }
}
