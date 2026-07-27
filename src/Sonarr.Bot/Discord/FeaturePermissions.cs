using Discord;

namespace Sonarr.Bot.Discord;

/// <summary>
/// What each feature actually needs from Discord, so <c>/checkperms</c> can tell an
/// admin "levels will silently do nothing" before users find out
/// (docs/checklist.md — "Kill switches &amp; health").
/// </summary>
public sealed record FeatureRequirement(string Feature, GuildPermission[] Permissions, string Consequence);

public static class FeaturePermissions
{
    public static readonly FeatureRequirement[] All =
    [
        new("Chat", [GuildPermission.ViewChannel, GuildPermission.SendMessages],
            "she can't answer mentions"),
        new("Chat reactions", [GuildPermission.AddReactions],
            "reaction-only replies are dropped"),
        new("Music", [GuildPermission.Connect, GuildPermission.Speak, GuildPermission.ViewChannel],
            "she can't join or play in voice"),
        new("Levels", [GuildPermission.ManageRoles],
            "level-up role rewards can't be granted"),
        new("Moderation", [GuildPermission.KickMembers, GuildPermission.BanMembers,
                           GuildPermission.ModerateMembers, GuildPermission.ManageMessages],
            "warn/kick/ban/timeout/purge will fail"),
        new("Welcome + autorole", [GuildPermission.ManageRoles, GuildPermission.SendMessages],
            "joins get no greeting and no role"),
        new("Slowmode", [GuildPermission.ManageChannels],
            "/slowmode can't change the channel"),
        new("Tickets", [GuildPermission.CreatePrivateThreads, GuildPermission.SendMessagesInThreads],
            "/ticket can't open a thread"),
        new("Events", [GuildPermission.CreateEvents],
            "/event can't create server events"),
        new("Embeds", [GuildPermission.EmbedLinks],
            "rank cards, queues and now-playing come out as plain text"),
    ];
}
