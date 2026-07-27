using Discord;
using Discord.Interactions;
using Sonarr.Domain.Caching;

namespace Sonarr.Bot.Discord.Preconditions;

/// <summary>
/// The global command flood guard: one command per user per 10 s window, backed by
/// <c>rl:cmd:{user}</c> (docs/05-caching.md area <c>rl</c>,
/// docs/checklist.md — "Global command flood guard").
/// </summary>
/// <remarks>
/// <para>
/// Applied to every command by living on <see cref="SonarrModuleBase{TContext}"/>, which all
/// modules derive from — Discord.Net's module builder reads type attributes with
/// <c>inherit: true</c>, so one declaration on the base class covers the whole surface.
/// </para>
/// <para>
/// <b>Redis outage blocks commands.</b> <see cref="ICooldownStore"/> fails closed by contract:
/// when the cache is unreachable <c>TryAcquireCommandAsync</c> returns <c>false</c> and every
/// command answers "slow down". That is the accepted trade-off — the same interface guards the
/// web DM-token endpoint, where "cache down" must never mean "unlimited", and one consistent
/// failure mode beats a per-method exception list nobody remembers. The user-visible cost is a
/// brief refusal with a friendly message; Redis lives in the same compose stack as the bot.
/// </para>
/// <para>
/// Follows from that: <c>/ping</c> and <c>/status</c> stay on the plain
/// <see cref="InteractionModuleBase{T}"/>. They are the "must work even when everything else is
/// off" pair, and a fail-closed guard would silence them during exactly the outage you need them
/// to report on. Every other module derives from <see cref="SonarrModuleBase{TContext}"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireCommandRateLimitAttribute : PreconditionAttribute
{
    /// <summary>Shown to the user verbatim by the error pipeline (no case id, it is expected).</summary>
    public const string TooFast = "Slow down a moment — try that again in a few seconds.";

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);

        var cooldowns = services.GetService<ICooldownStore>();
        if (cooldowns is null)
        {
            // No store registered (unit tests, or a host that skipped AddSonarrRedis): the
            // guard is not the place to fail a command over missing wiring.
            return PreconditionResult.FromSuccess();
        }

        var allowed = await cooldowns.TryAcquireCommandAsync(context.User.Id).ConfigureAwait(false);
        return allowed
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError(TooFast);
    }
}
