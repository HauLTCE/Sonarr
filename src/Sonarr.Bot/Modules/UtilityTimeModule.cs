using System.Globalization;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/timezone</c> and <c>/timestamp</c> (docs/07-commands.md#utility) — the two commands that
/// make "your time" mean the same thing everywhere Sonarr shows a time.
/// </summary>
public sealed class UtilityTimeModule(IReminderService reminders) : SonarrModuleBase<SocketInteractionContext>
{
    [SlashCommand("timezone", "Tell me your timezone so reminders and times land right.")]
    public async Task TimezoneAsync(
        [Summary("zone", "IANA id like Asia/Ho_Chi_Minh. Leave empty to clear.")]
        [Autocomplete(typeof(TimezoneAutocompleteHandler))] string? zone = null)
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Your timezone is stored per server — run this in one.");
            return;
        }

        // Validation is shape-based in the service, not TimeZoneInfo.FindSystemTimeZoneById:
        // InvariantGlobalization strips ICU, so a Windows dev box resolves no IANA id at all.
        ScheduleResult result = await reminders.SetTimezoneAsync(Context.Guild.Id, Context.User.Id, zone);

        if (result.Success)
        {
            await RespondPersonalAsync(result.Message);
            return;
        }

        await RespondInvalidAsync(result.Message);
    }

    [SlashCommand("timestamp", "Turn a time into a Discord timestamp everyone reads in their own zone.")]
    public async Task TimestampAsync(
        [Summary("time", "9am, 21:30, tomorrow 8am, in 2h, 2026-07-30 18:00")] string time,
        [Summary("format", "How it should read")]
        [Choice("relative — in 3 hours", "R")]
        [Choice("short time — 18:00", "t")]
        [Choice("long time — 18:00:00", "T")]
        [Choice("short date — 30/07/2026", "d")]
        [Choice("long date — 30 July 2026", "D")]
        [Choice("date and time", "f")]
        [Choice("full — Thursday, 30 July 2026 18:00", "F")]
        string format = "f")
    {
        var guildId = Context.Guild?.Id ?? 0UL;
        TimeZoneInfo zone = await reminders.ResolveZoneAsync(guildId, Context.User.Id);

        if (!WhenParser.TryParse(time, DateTimeOffset.UtcNow, zone, out WhenResult? parsed, out var error))
        {
            await RespondInvalidAsync(error);
            return;
        }

        var unix = parsed!.RunAt.ToUnixTimeSeconds();

        // Public on purpose: a timestamp exists to be shared, and the copyable source goes with it
        // so anyone can paste it into their own message.
        await RespondPublicAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"<t:{unix}:{format}>  ·  `<t:{unix}:{format}>`"));
    }
}

/// <summary>
/// Autocomplete for <c>/timezone</c>, served from <see cref="IanaId.Match"/>. Deliberately not
/// backed by <c>TimeZoneInfo.GetSystemTimeZones()</c>: with ICU stripped that list is empty on
/// Windows and Windows-named in other configurations, so the picker would differ between the dev
/// box and the container.
/// </summary>
public sealed class TimezoneAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = IanaId.Match(typed)
            .Select(z => new AutocompleteResult(z, z));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}
