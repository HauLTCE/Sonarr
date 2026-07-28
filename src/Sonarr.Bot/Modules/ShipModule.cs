using Discord;
using Discord.Interactions;
using Sonarr.Application.Utility;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Configuration;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/ship</c> (docs/07-commands.md#community). Thin translator over <see cref="ShipMeter"/>:
/// the number and the snark are both computed there, this draws the bar.
/// </summary>
/// <remarks>
/// Public, because shipping two people is a joke for the room — but with
/// <see cref="AllowedMentions.None"/>, so being shipped is not a notification. Nothing is stored.
/// </remarks>
[RequireContext(ContextType.Guild)]
[RequireFeature(FeatureNames.Social)]
public sealed class ShipModule(ShipMeter ships) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Segments in the bar. Ten keeps it one line on mobile.</summary>
    public const int BarSegments = 10;

    [SlashCommand("ship", "Rate the compatibility of two people. Badly.")]
    public async Task ShipAsync(
        [Summary("user_a", "One half")] IUser userA,
        [Summary("user_b", "The other half")] IUser userB)
    {
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(userA);
        ArgumentNullException.ThrowIfNull(userB);

        if (userA.Id == userB.Id)
        {
            await RespondInvalidAsync("That's one person. Pick two.");
            return;
        }

        ShipVerdict verdict = ships.Rate((long)userA.Id, (long)userB.Id);
        string? name = ShipMeter.Name(Display(userA), Display(userB));

        Embed embed = new EmbedBuilder()
            // The names go in the title, where markdown does not render — one less place a
            // display name can inject formatting into a public message.
            .WithTitle(name is null ? "Ship" : Format.Sanitize(name))
            .WithDescription($"{Bar(verdict.Percent)} **{verdict.Percent}%**\n\n{verdict.Line}")
            .WithColor(Tint(verdict.Percent))
            .Build();

        await RespondAsync(
            $"{Format.Sanitize(Display(userA))} × {Format.Sanitize(Display(userB))}",
            embed: embed,
            allowedMentions: AllowedMentions.None);
    }

    private static string Bar(int percent)
    {
        int filled = percent * BarSegments / 100;
        return new string('█', filled) + new string('░', BarSegments - filled);
    }

    private static Color Tint(int percent) => percent <= ShipMeter.LowCeiling
        ? new Color(0x9B9B9B)
        : percent <= ShipMeter.MidCeiling ? new Color(0xE8A13A) : new Color(0xE8467C);

    /// <summary>
    /// Nickname when they have one, username otherwise — the same name the room already sees.
    /// </summary>
    private static string Display(IUser user) =>
        (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username;
}
