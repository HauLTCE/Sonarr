using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Sonarr.Bot.Observability;

// The Web SDK's implicit usings bring in Microsoft.AspNetCore.Http.IResult, which
// collides with Discord's command-result type.
using IResult = Discord.Interactions.IResult;

namespace Sonarr.Bot.Discord;

/// <summary>
/// Routes incoming interactions into the InteractionService and owns the error
/// pipeline: full detail to Serilog, a friendly one-liner plus a case id to the user.
/// Stack traces never reach Discord (docs/02-architecture.md#cross-cutting).
/// </summary>
public sealed class InteractionHandler(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    SonarrMetrics metrics,
    UserErrorLog errors,
    ILogger<InteractionHandler> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.InteractionCreated += OnInteractionAsync;
        interactions.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.InteractionCreated -= OnInteractionAsync;
        interactions.SlashCommandExecuted -= OnSlashCommandExecutedAsync;
        return Task.CompletedTask;
    }

    private async Task OnInteractionAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        try
        {
            await interactions.ExecuteCommandAsync(context, services);
        }
        catch (Exception ex)
        {
            await ReportAsync(interaction, ex, interaction.Type.ToString());
        }
    }

    private async Task OnSlashCommandExecutedAsync(
        SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
        {
            metrics.Increment(MetricNames.CommandsExecuted);
            return;
        }

        metrics.Increment(MetricNames.CommandsFailed);

        // Precondition failures (missing perms, kill switch) are expected outcomes,
        // not incidents: show the reason as-is, don't mint a case id.
        if (result.Error is InteractionCommandError.UnmetPrecondition)
        {
            await RespondAsync(context.Interaction, result.ErrorReason, ephemeral: true);
            return;
        }

        var exception = (result as ExecuteResult?)?.Exception;
        await ReportAsync(context.Interaction, exception, command.Name, result.ErrorReason);
    }

    private async Task ReportAsync(
        IDiscordInteraction interaction, Exception? exception, string what, string? reason = null)
    {
        // Correlates the user-visible line with the log entry. Not a mod case id —
        // mod.case is a different, durable thing (docs/04-database.md#modcase).
        var caseId = Guid.NewGuid().ToString("N")[..8];

        log.LogError(exception, "Interaction {What} failed (case {CaseId}): {Reason}",
            what, caseId, reason ?? exception?.Message ?? "unknown");

        var friendly = $"Something broke on my end. Reference `{caseId}` if you want someone to look at it.";

        // Same line, kept for the panel's "My errors" page — the reason a user does not need an
        // errors channel on Discord (docs/09). The exception itself stays in the log.
        errors.Record(
            interaction.User?.Id ?? 0,
            new UserError(caseId, what, friendly, DateTimeOffset.UtcNow));

        await RespondAsync(interaction, friendly, ephemeral: true);
    }

    /// <summary>Errors are always ephemeral (docs/07-commands.md#design-rules).</summary>
    private async Task RespondAsync(IDiscordInteraction interaction, string message, bool ephemeral)
    {
        try
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(message, ephemeral: ephemeral);
            }
            else
            {
                await interaction.RespondAsync(message, ephemeral: ephemeral);
            }
        }
        catch (Exception ex)
        {
            // The interaction token can be dead (3 s / 15 min windows). Nothing to do
            // but record it — never let the error path throw.
            log.LogWarning(ex, "Could not deliver the error message to the user");
        }
    }
}
