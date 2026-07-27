using Microsoft.Extensions.Logging.Abstractions;
using Sonarr.Application.Moderation;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Tests.Moderation;

/// <summary>
/// AntiSpam v2: each of the three triggers, the exemptions, and the behaviour when Redis is
/// unreachable.
/// </summary>
public sealed class AntiSpamServiceTests
{
    [Fact]
    public async Task An_ordinary_message_is_clean()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("hello everyone, how's it going"));

        Assert.False(verdict.IsSpam);
        Assert.Equal(SpamTrigger.None, verdict.Trigger);
    }

    [Fact]
    public async Task Identical_flood_trips_at_the_configured_threshold()
    {
        (AntiSpamService antiSpam, FakeCooldownStore cooldowns) = Service();

        SpamVerdict? last = null;
        for (var i = 0; i < ModerationPolicy.Default.IdenticalFloodThreshold; i++)
        {
            last = await antiSpam.EvaluateAsync(Message("BUY GOLD NOW"));
        }

        Assert.Equal(SpamTrigger.IdenticalFlood, last!.Trigger);
        Assert.Equal("4", last.Detail!["repeats"]);

        // The window is cleared so the same burst does not re-fire on every later message.
        Assert.Equal(1, cooldowns.Cleared);
    }

    [Fact]
    public async Task Identical_flood_does_not_trip_below_the_threshold()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict? last = null;
        for (var i = 0; i < ModerationPolicy.Default.IdenticalFloodThreshold - 1; i++)
        {
            last = await antiSpam.EvaluateAsync(Message("hi"));
        }

        Assert.False(last!.IsSpam);
    }

    [Fact]
    public async Task Whitespace_and_case_do_not_dodge_the_flood_rule()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict a = await antiSpam.EvaluateAsync(Message("Buy Gold"));
        SpamVerdict b = await antiSpam.EvaluateAsync(Message("buygold"));
        SpamVerdict c = await antiSpam.EvaluateAsync(Message("B U Y   g o l d"));
        SpamVerdict d = await antiSpam.EvaluateAsync(Message("buy   GOLD"));

        Assert.False(a.IsSpam);
        Assert.False(b.IsSpam);
        Assert.False(c.IsSpam);
        Assert.Equal(SpamTrigger.IdenticalFlood, d.Trigger);
    }

    [Fact]
    public async Task An_attachment_only_message_is_never_a_flood()
    {
        (AntiSpamService antiSpam, _) = Service();

        for (var i = 0; i < 10; i++)
        {
            SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("   "));
            Assert.False(verdict.IsSpam);
        }
    }

    [Fact]
    public async Task Mass_mention_trips_on_the_mention_count()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict verdict = await antiSpam.EvaluateAsync(
            Message("look at this") with { MentionedUserCount = 5, MentionedRoleCount = 2 });

        Assert.Equal(SpamTrigger.MassMention, verdict.Trigger);
        Assert.Equal("7", verdict.Detail!["mentions"]);
    }

    [Fact]
    public async Task Everyone_from_a_non_staff_member_trips_mass_mention()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict verdict = await antiSpam.EvaluateAsync(
            Message("@everyone free stuff") with { MentionsEveryone = true });

        Assert.Equal(SpamTrigger.MassMention, verdict.Trigger);
    }

    [Theory]
    [InlineData("join us at discord.gg/abc123")]
    [InlineData("https://discord.com/invite/whatever")]
    [InlineData("HTTPS://DISCORDAPP.COM/INVITE/Cool-Server")]
    [InlineData("discord.me/somewhere")]
    public async Task Invite_links_trip_the_invite_rule(string content)
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message(content));

        Assert.Equal(SpamTrigger.InviteLink, verdict.Trigger);
    }

    [Fact]
    public async Task Talking_about_discord_is_not_an_invite()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("discord.com is down again"));

        Assert.False(verdict.IsSpam);
    }

    [Fact]
    public async Task Invite_links_are_allowed_when_the_guild_turns_the_rule_off()
    {
        FakeGuildConfigService config = new FakeGuildConfigService()
            .With(ModerationConfigKeys.BlockInvites, "false");

        (AntiSpamService antiSpam, _) = Service(config);

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("discord.gg/abc123"));

        Assert.False(verdict.IsSpam);
    }

    [Fact]
    public async Task Moderators_and_bots_are_exempt()
    {
        (AntiSpamService antiSpam, _) = Service();

        SpamVerdict staff = await antiSpam.EvaluateAsync(
            Message("discord.gg/abc123") with { AuthorIsModerator = true });
        SpamVerdict bot = await antiSpam.EvaluateAsync(
            Message("discord.gg/abc123") with { AuthorIsBot = true });

        Assert.False(staff.IsSpam);
        Assert.False(bot.IsSpam);
    }

    [Fact]
    public async Task Everything_is_clean_when_anti_spam_is_disabled()
    {
        FakeGuildConfigService config = new FakeGuildConfigService()
            .With(ModerationConfigKeys.AntiSpamEnabled, "false");

        (AntiSpamService antiSpam, _) = Service(config);

        SpamVerdict verdict = await antiSpam.EvaluateAsync(
            Message("discord.gg/abc") with { MentionsEveryone = true });

        Assert.False(verdict.IsSpam);
    }

    [Fact]
    public async Task The_configured_action_and_timeout_come_back_on_the_verdict()
    {
        FakeGuildConfigService config = new FakeGuildConfigService()
            .With(ModerationConfigKeys.Action, "timeout")
            .With(ModerationConfigKeys.TimeoutMinutes, "15");

        (AntiSpamService antiSpam, _) = Service(config);

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("discord.gg/abc123"));

        Assert.Equal(SpamAction.Timeout, verdict.Action);
        Assert.Equal(TimeSpan.FromMinutes(15), verdict.TimeoutFor);
    }

    [Fact]
    public async Task A_non_timeout_action_carries_no_timeout_length()
    {
        FakeGuildConfigService config = new FakeGuildConfigService()
            .With(ModerationConfigKeys.Action, "kick");

        (AntiSpamService antiSpam, _) = Service(config);

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("discord.gg/abc123"));

        Assert.Equal(SpamAction.Kick, verdict.Action);
        Assert.Null(verdict.TimeoutFor);
    }

    [Fact]
    public async Task A_redis_outage_never_punishes_a_user_for_flooding()
    {
        (AntiSpamService antiSpam, FakeCooldownStore cooldowns) = Service();
        cooldowns.Unavailable = true;

        for (var i = 0; i < 20; i++)
        {
            SpamVerdict verdict = await antiSpam.EvaluateAsync(Message("same thing over and over"));
            Assert.False(verdict.IsSpam);
        }
    }

    [Fact]
    public async Task Content_rules_keep_working_during_a_redis_outage()
    {
        (AntiSpamService antiSpam, FakeCooldownStore cooldowns) = Service();
        cooldowns.Unavailable = true;

        SpamVerdict invite = await antiSpam.EvaluateAsync(Message("discord.gg/abc123"));
        SpamVerdict mentions = await antiSpam.EvaluateAsync(
            Message("hi") with { MentionedUserCount = 9 });

        Assert.Equal(SpamTrigger.InviteLink, invite.Trigger);
        Assert.Equal(SpamTrigger.MassMention, mentions.Trigger);
    }

    [Fact]
    public async Task A_verdict_never_carries_message_content()
    {
        (AntiSpamService antiSpam, _) = Service();

        const string Secret = "discord.gg/verysecretinvitecode";

        SpamVerdict verdict = await antiSpam.EvaluateAsync(Message(Secret));

        Assert.DoesNotContain("verysecretinvitecode", verdict.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "verysecretinvitecode",
            string.Join("|", verdict.Detail!.Values),
            StringComparison.Ordinal);
    }

    private static SpamCandidate Message(string content)
        => new(Build.Guild, ChannelId: 900UL, Build.Target, content);

    private static (AntiSpamService AntiSpam, FakeCooldownStore Cooldowns) Service(
        FakeGuildConfigService? config = null)
    {
        (Sonarr.Application.Moderation.ModerationService moderation, _, _) =
            Moderation.Build.Moderation(config);
        FakeCooldownStore cooldowns = new();

        return (new AntiSpamService(moderation, cooldowns, NullLogger<AntiSpamService>.Instance), cooldowns);
    }
}
