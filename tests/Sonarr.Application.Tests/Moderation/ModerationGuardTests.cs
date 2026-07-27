using Sonarr.Application.Moderation;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Tests.Moderation;

/// <summary>
/// Role hierarchy and permission rules. This is the security boundary the checklist calls out:
/// you cannot moderate someone above you, and neither can Sonarr.
/// </summary>
public sealed class ModerationGuardTests
{
    private const ulong Actor = 1UL;
    private const ulong Target = 2UL;

    [Fact]
    public void Actor_outranking_the_target_is_allowed()
        => Assert.Equal(
            ModerationDenial.None,
            ModerationGuard.Check(CaseAction.Ban, Actor, Target, Snapshot(actor: 10, target: 4)));

    [Fact]
    public void Actor_below_the_target_is_refused()
        => Assert.Equal(
            ModerationDenial.ActorRoleTooLow,
            ModerationGuard.Check(CaseAction.Ban, Actor, Target, Snapshot(actor: 3, target: 9)));

    [Fact]
    public void Equal_top_roles_are_refused()
        => Assert.Equal(
            ModerationDenial.ActorRoleTooLow,
            ModerationGuard.Check(CaseAction.Kick, Actor, Target, Snapshot(actor: 7, target: 7)));

    [Fact]
    public void Bot_below_the_target_is_refused_even_when_the_mod_outranks_them()
        => Assert.Equal(
            ModerationDenial.BotRoleTooLow,
            ModerationGuard.Check(CaseAction.Ban, Actor, Target, Snapshot(actor: 20, target: 9, bot: 5)));

    [Fact]
    public void Guild_owner_may_moderate_a_higher_role_than_their_own_position()
        => Assert.Equal(
            ModerationDenial.None,
            ModerationGuard.Check(
                CaseAction.Ban, Actor, Target, Snapshot(actor: 1, target: 9, bot: 20) with { ActorIsOwner = true }));

    [Fact]
    public void Missing_actor_permission_is_refused_first()
        => Assert.Equal(
            ModerationDenial.ActorMissingPermission,
            ModerationGuard.Check(
                CaseAction.Ban, Actor, Target,
                Snapshot(actor: 20, target: 1) with { ActorHasPermission = false }));

    [Fact]
    public void Missing_bot_permission_is_refused()
        => Assert.Equal(
            ModerationDenial.BotMissingPermission,
            ModerationGuard.Check(
                CaseAction.Ban, Actor, Target,
                Snapshot(actor: 20, target: 1) with { BotHasPermission = false }));

    [Fact]
    public void Self_target_is_refused()
        => Assert.Equal(
            ModerationDenial.SelfTarget,
            ModerationGuard.Check(CaseAction.Warn, Actor, Actor, Snapshot(actor: 10, target: 1)));

    [Fact]
    public void The_guild_owner_cannot_be_targeted()
        => Assert.Equal(
            ModerationDenial.GuildOwnerTarget,
            ModerationGuard.Check(
                CaseAction.Ban, Actor, Target, Snapshot(actor: 20, target: 1) with { TargetIsOwner = true }));

    [Fact]
    public void Sonarr_will_not_moderate_itself()
        => Assert.Equal(
            ModerationDenial.BotTarget,
            ModerationGuard.Check(
                CaseAction.Ban, Actor, Target, Snapshot(actor: 20, target: 1) with { TargetIsBot = true }));

    [Fact]
    public void An_absent_member_can_still_be_banned_by_id()
        => Assert.Equal(
            ModerationDenial.None,
            ModerationGuard.Check(CaseAction.Ban, Actor, Target, Snapshot(actor: 10, target: null)));

    [Theory]
    [InlineData(CaseAction.Warn)]
    [InlineData(CaseAction.Kick)]
    [InlineData(CaseAction.Timeout)]
    [InlineData(CaseAction.Untimeout)]
    public void An_absent_member_cannot_be_warned_kicked_or_timed_out(CaseAction action)
        => Assert.Equal(
            ModerationDenial.TargetNotFound,
            ModerationGuard.Check(action, Actor, Target, Snapshot(actor: 10, target: null)));

    [Fact]
    public void Every_denial_has_its_own_sentence()
    {
        List<string> sentences = [.. Enum.GetValues<ModerationDenial>()
            .Where(d => d is not ModerationDenial.None)
            .Select(ModerationGuard.Explain)];

        Assert.All(sentences, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    private static HierarchySnapshot Snapshot(int actor, int? target, int bot = 30) => new(
        ActorHasPermission: true,
        BotHasPermission: true,
        ActorIsOwner: false,
        ActorTopRole: actor,
        BotTopRole: bot,
        TargetTopRole: target);
}
