using System.Globalization;
using System.Security.Cryptography;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/choose</c>, <c>/roll</c>, <c>/color</c>, <c>/enlarge</c> (docs/07-commands.md#utility).
/// Stateless one-liners: nothing here touches the database, Redis or the persona.
/// </summary>
public sealed class FunModule : SonarrModuleBase<SocketInteractionContext>
{
    private const int MaxOptions = 20;
    private const int MaxDice = 100;
    private const int MaxSides = 1000;

    [SlashCommand("choose", "Pick one of your options for you.")]
    public async Task ChooseAsync(
        [Summary("options", "Separate them with commas or |")] string options)
    {
        string[] choices =
        [
            .. (options ?? string.Empty)
                .Split(['|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxOptions + 1),
        ];

        if (choices.Length < 2)
        {
            await RespondInvalidAsync("Give me at least two options, separated by commas.");
            return;
        }

        if (choices.Length > MaxOptions)
        {
            await RespondInvalidAsync($"That's more than {MaxOptions} options. Narrow it down first.");
            return;
        }

        var pick = choices[RandomNumberGenerator.GetInt32(choices.Length)];
        await RespondPublicAsync($"**{Escape(pick)}**");
    }

    [SlashCommand("roll", "Roll dice. 2d6, 1d20, that sort of thing.")]
    public async Task RollAsync(
        [Summary("dice", "Like 2d6 or d20. Defaults to 1d6.")] string dice = "1d6")
    {
        if (!TryParseDice(dice, out var count, out var sides, out var problem))
        {
            await RespondInvalidAsync(problem);
            return;
        }

        var rolls = new int[count];
        long total = 0;
        for (var i = 0; i < count; i++)
        {
            rolls[i] = RandomNumberGenerator.GetInt32(1, sides + 1);
            total += rolls[i];
        }

        var detail = count == 1
            ? string.Empty
            : $" ({string.Join(" + ", rolls)})";

        await RespondPublicAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"🎲 `{count}d{sides}` → **{total}**{detail}"));
    }

    [SlashCommand("color", "Preview a colour. Hex, or leave empty for a random one.")]
    public async Task ColorAsync(
        [Summary("hex", "Like #5865F2. Empty for a surprise.")] string? hex = null)
    {
        uint value;
        if (string.IsNullOrWhiteSpace(hex))
        {
            value = (uint)RandomNumberGenerator.GetInt32(0x1000000);
        }
        else if (!TryParseHex(hex, out value))
        {
            await RespondInvalidAsync($"`{hex.Trim()}` isn't a hex colour. Try `#5865F2`.");
            return;
        }

        var code = $"#{value:X6}";
        var (r, g, b) = ((byte)(value >> 16), (byte)(value >> 8), (byte)value);

        Embed embed = new EmbedBuilder()
            .WithTitle(code)
            .WithDescription(string.Create(CultureInfo.InvariantCulture, $"RGB {r}, {g}, {b}"))
            .WithColor(new Color(value))
            // The swatch: a 1px image tinted by the service, scaled by Discord's viewer.
            .WithThumbnailUrl($"https://singlecolorimage.com/get/{value:x6}/128x128")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("enlarge", "Show a custom emoji at full size.")]
    public async Task EnlargeAsync(
        [Summary("emoji", "A custom emoji from this server or any Sonarr shares")] string emoji)
    {
        if (!Emote.TryParse(emoji?.Trim(), out Emote? parsed))
        {
            // Unicode emoji have no image to enlarge — only custom ones do.
            await RespondInvalidAsync("That's not a custom emoji. Paste one like `:sonarr:` from the picker.");
            return;
        }

        Embed embed = new EmbedBuilder()
            .WithTitle($":{parsed.Name}:")
            .WithImageUrl(parsed.Url)
            .WithUrl(parsed.Url)
            .WithColor(new Color(0x5865F2))
            .Build();

        await RespondAsync(embed: embed);
    }

    private static bool TryParseDice(string? input, out int count, out int sides, out string problem)
    {
        count = 0;
        sides = 0;
        problem = string.Empty;

        var text = (input ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        var d = text.IndexOf('d', StringComparison.Ordinal);
        if (d < 0)
        {
            problem = "Dice look like `2d6` or `d20`.";
            return false;
        }

        var left = text[..d];
        var right = text[(d + 1)..];

        count = left.Length == 0 ? 1 : int.TryParse(left, CultureInfo.InvariantCulture, out var c) ? c : 0;
        sides = int.TryParse(right, CultureInfo.InvariantCulture, out var s) ? s : 0;

        if (count is < 1 || sides < 2)
        {
            problem = "Dice look like `2d6` or `d20` — at least one die with at least two sides.";
            return false;
        }

        if (count > MaxDice || sides > MaxSides)
        {
            problem = $"Keep it to {MaxDice} dice with {MaxSides} sides at most.";
            return false;
        }

        return true;
    }

    private static bool TryParseHex(string hex, out uint value)
        => uint.TryParse(
            hex.Trim().TrimStart('#').TrimStart('0', 'x'),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out value) && value <= 0xFFFFFF;

    /// <summary>
    /// A picked option is user text going into a public message: neutralise markdown and mentions
    /// so nobody can use /choose to ping the server.
    /// </summary>
    private static string Escape(string text)
    {
        var clipped = text.Length > 200 ? text[..200] : text;
        return Format.Sanitize(clipped).Replace("@", "@​", StringComparison.Ordinal);
    }
}
