using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/checkperms</c> — the bot audits its own permissions per feature, so a broken
/// feature reports itself instead of failing silently
/// (docs/checklist.md — "Kill switches &amp; health").
/// </summary>
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
// DefaultMemberPermissions is a default a server admin can override; this is the enforcement.
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class PermissionsModule : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("checkperms", "Check what I'm allowed to do here, feature by feature.")]
    public async Task CheckPermsAsync()
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Run this in a server — there's nothing to check in a DM.");
            return;
        }

        var me = Context.Guild.CurrentUser;
        var report = new StringBuilder();
        var missingAny = false;

        foreach (var requirement in FeaturePermissions.All)
        {
            var missing = requirement.Permissions
                .Where(p => !me.GuildPermissions.Has(p))
                .ToArray();

            if (missing.Length == 0)
            {
                report.AppendLine($"✅ **{requirement.Feature}**");
                continue;
            }

            missingAny = true;
            report.AppendLine(
                $"❌ **{requirement.Feature}** — missing {string.Join(", ", missing)} → {requirement.Consequence}");
        }

        if (missingAny)
        {
            report.AppendLine();
            report.AppendLine("Guild-level permissions only; a channel override can still block me somewhere specific.");
        }

        await RespondPersonalAsync(report.ToString());
    }
}
