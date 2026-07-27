using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Sonarr.Bot.Discord;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Modules;

/// <summary>
/// <c>/remind</c> and <c>/reminders</c> (docs/07-commands.md#utility). Every reminder is a
/// <c>core.job</c> row, so a restart loses nothing — this module only parses input, calls the
/// service and formats the answer.
/// </summary>
public sealed class RemindersModule(IReminderService reminders) : SonarrModuleBase<SocketInteractionContext>
{
    /// <summary>Matches <c>ReminderService.MaxTextLength</c>; the service is still the authority.</summary>
    private const int MaxTextLength = 1000;

    [SlashCommand("remind", "Remind you about something later. \"in 2h\", \"tomorrow 9am\", \"every monday 09:00\".")]
    [RequireFeature(FeatureNames.Reminders)]
    public async Task RemindAsync(
        [Summary("when", "When to nudge you: 10m, 2h30m, tomorrow 8am, every day 9am")] string when,
        [Summary("what", "What to remind you about")] string what)
    {
        if (Context.Guild is null)
        {
            await RespondInvalidAsync("Reminders are per server — run this in one.");
            return;
        }

        if (InputGuards.Length(what, MaxTextLength, "reminder") is { } problem)
        {
            await RespondInvalidAsync(problem);
            return;
        }

        ScheduleResult result = await reminders.CreateAsync(
            Context.Guild.Id, Context.Channel.Id, Context.User.Id, when, what);

        await RespondPersonalAsync(result.Message);
    }

    [Group("reminders", "See and cancel your reminders.")]
    public sealed class RemindersGroup(IReminderService reminders)
        : SonarrModuleBase<SocketInteractionContext>
    {
        [SlashCommand("list", "Your pending reminders, soonest first.")]
        public async Task ListAsync()
        {
            IReadOnlyList<ReminderView> pending = await reminders.ListAsync(Context.User.Id);

            if (pending.Count == 0)
            {
                await RespondPersonalAsync("Nothing pending. Enjoy the quiet.");
                return;
            }

            var report = new StringBuilder("**Your reminders**\n");
            foreach (ReminderView item in pending)
            {
                report.Append(CultureInfo.InvariantCulture, $"`#{item.JobId}` <t:{item.RunAt.ToUnixTimeSeconds()}:R>");
                if (item.Recurrence is not null)
                {
                    report.Append(CultureInfo.InvariantCulture, $" · {item.Schedule}");
                }

                report.AppendLine(CultureInfo.InvariantCulture, $" — {Trim(item.Text)}");
            }

            await RespondPersonalAsync(report.ToString());
        }

        [SlashCommand("cancel", "Cancel one of your reminders by its id.")]
        public async Task CancelAsync(
            [Summary("id", "The `#id` from /reminders list")]
            [Autocomplete(typeof(ReminderAutocompleteHandler))] long id)
        {
            // Ownership is enforced in the service's query, not by trusting this id.
            var cancelled = await reminders.CancelAsync(Context.User.Id, id);

            await RespondPersonalAsync(cancelled
                ? $"`#{id}` cancelled."
                : $"No pending reminder `#{id}` of yours. Check `/reminders list`.");
        }

        /// <summary>Keeps a reminder line inside Discord's 2000-character message budget.</summary>
        private static string Trim(string text)
        {
            var single = text.ReplaceLineEndings(" ");
            return single.Length <= 80 ? single : single[..77] + "...";
        }
    }
}

/// <summary>
/// Autocomplete over the caller's own pending reminders, so <c>/reminders cancel</c> never needs an
/// id typed by hand (docs/07-commands.md#design-rules: autocomplete wherever values are known).
/// </summary>
public sealed class ReminderAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(services);

        var reminders = services.GetRequiredService<IReminderService>();
        IReadOnlyList<ReminderView> pending = await reminders.ListAsync(context.User.Id);

        var typed = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = pending
            .Select(r => new
            {
                r.JobId,
                Label = Label(r),
            })
            .Where(r => typed.Length == 0 || r.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            // The option is a long, so the value must be too — a string here fails Discord's
            // type check silently.
            .Select(r => new AutocompleteResult(r.Label, r.JobId));

        return AutocompletionResult.FromSuccess(matches);
    }

    private static string Label(ReminderView reminder)
    {
        var text = reminder.Text.ReplaceLineEndings(" ");
        var head = $"#{reminder.JobId} · {reminder.RunAt:yyyy-MM-dd HH:mm} UTC · ";
        var room = 100 - head.Length;

        return head + (text.Length <= room ? text : text[..Math.Max(0, room - 3)] + "...");
    }
}
