using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/privacy</c> (docs/06-data-and-privacy.md). Ephemeral: what Sonarr keeps about you is your
/// business, not the channel's.
/// </summary>
/// <remarks>
/// The text itself lives in <see cref="PrivacyNotice"/> so the command and the doc cannot drift
/// apart — a test reads the doc and checks the notice against it.
/// </remarks>
public sealed class PrivacyModule : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("privacy", "What I keep about you, how long, and how to get rid of it.")]
    public Task PrivacyAsync()
    {
        // The notice runs past Discord's 2000-character message limit, so it goes in an embed
        // description (4096) rather than being cut in half.
        return RespondAsync(
            embed: new global::Discord.EmbedBuilder()
                .WithTitle("Your data")
                .WithDescription(PrivacyNotice.Render())
                .WithColor(new global::Discord.Color(0x5865F2))
                .Build(),
            ephemeral: true);
    }
}
