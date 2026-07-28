using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using Sonarr.Bot.Configuration;
using Sonarr.Domain.Web;

namespace Sonarr.Bot.Discord.Web;

/// <summary>
/// Delivers a login code by DM (docs/09). Registered separately from the auth service because it is
/// the one part of the login flow that needs the gateway.
/// </summary>
/// <remarks>
/// Best-effort by design: closed DMs are a supported outcome, not an error. The visitor is told
/// nothing either way, and the login page carries the help box explaining the DMs-open requirement,
/// so a failure here must not change the API response.
/// </remarks>
public sealed class LoginTokenSender(
    DiscordSocketClient client,
    IOptions<SonarrOptions> options,
    ILogger<LoginTokenSender> log)
{
    /// <summary>The DM text. Static so its wording can be asserted without a gateway.</summary>
    public static string Body(string token, string panelUrl)
        => $"Someone is logging into {panelUrl} as you.\n"
            + $"Your code: **{WebAuthRules.Format(token)}** (valid 10 minutes).\n"
            + "Not you? Ignore this message — the code is useless without your browser.";

    /// <summary>True when the DM went out. False means closed DMs or an unknown user.</summary>
    public async Task<bool> SendAsync(ulong userId, string token, CancellationToken ct = default)
    {
        try
        {
            // Cache first, REST as the fallback: the member is in a shared guild by construction
            // (the handle was resolved from core.member), but the cache may not have them yet.
            IUser? user = (IUser?)client.GetUser(userId)
                ?? await client.Rest.GetUserAsync(userId, new RequestOptions { CancelToken = ct });

            if (user is null)
            {
                log.LogInformation("Login code not delivered: user {UserId} not visible to the gateway", userId);
                return false;
            }

            await user.SendMessageAsync(Body(token, options.Value.PanelBaseUrl));
            return true;
        }
        catch (global::Discord.Net.HttpException ex)
        {
            // 50007 = "cannot send messages to this user" — the documented closed-DM case.
            log.LogInformation(ex, "Login code not delivered to {UserId} (DMs closed?)", userId);
            return false;
        }
    }
}
