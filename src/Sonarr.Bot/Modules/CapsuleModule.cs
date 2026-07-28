using Discord.Interactions;
using Sonarr.Domain.Abstractions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/capsule write</c> (docs/07-commands.md#community). Parses nothing itself — the service owns
/// the schedule, the caps and the wording.
/// </summary>
/// <remarks>
/// The confirmation is ephemeral: a capsule is a surprise, and echoing it into the channel now
/// would spoil the only thing the feature does.
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Social)]
[Group("capsule", "Send a message to this channel, later.")]
public sealed class CapsuleModule(ICapsuleService capsules) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Matches the <c>message</c> column and the service's own cap, which is the authority.</summary>
    private const int MaxMessageLength = 2048;

    [SlashCommand("write", "Write something for this channel to read at a future date.")]
    public async Task WriteAsync(
        [Summary("when", "When it opens: \"in 6 months\", \"1 jan 2027\", \"next friday 9am\"")] string when,
        [Summary("message", "What the channel reads then")] string message)
    {
        ArgumentNullException.ThrowIfNull(capsules);

        if (InputGuards.Length(message, MaxMessageLength, "capsule") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        ScheduleResult result = await capsules.WriteAsync(
            Context.Guild.Id, Context.Channel.Id, Context.User.Id, when, message);

        await RespondPersonalAsync(result.Message);
    }
}
