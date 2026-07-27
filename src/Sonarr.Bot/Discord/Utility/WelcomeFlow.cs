using Discord;
using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Utility;

namespace Sonarr.Bot.Discord.Utility;

/// <summary>
/// WelcomeFlow (docs/08-background-services.md): posts the welcome/farewell embed and grants the
/// autorole when a member joins or leaves.
/// </summary>
/// <remarks>
/// <para>
/// The wording and the config reads live in <see cref="IWelcomeService"/>; this only performs the
/// plan. When no <c>welcome_channel</c> and no <c>autorole_id</c> are configured the plan is a
/// no-op and nothing is written or fetched.
/// </para>
/// <para>
/// <b>Never blocks the gateway</b> — the work is detached and every exception becomes a log line, so
/// a missing permission on one guild cannot stall member dispatch or drop the connection.
/// </para>
/// </remarks>
public sealed class WelcomeFlow(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<WelcomeFlow> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.UserJoined += OnJoinAsync;
        client.UserLeft += OnLeftAsync;
        log.LogInformation("WelcomeFlow watching member joins and leaves");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.UserJoined -= OnJoinAsync;
        client.UserLeft -= OnLeftAsync;
        return Task.CompletedTask;
    }

    private Task OnJoinAsync(SocketGuildUser member)
    {
        if (member.IsBot || member.IsWebhook)
        {
            // A bot invited by staff does not need a greeting or a member role.
            return Task.CompletedTask;
        }

        _ = Task.Run(() => RunAsync(member.Guild, Snapshot(member.Guild, member), join: true));
        return Task.CompletedTask;
    }

    private Task OnLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot || user.IsWebhook)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(() => RunAsync(guild, Snapshot(guild, user), join: false));
        return Task.CompletedTask;
    }

    private async Task RunAsync(SocketGuild guild, MemberEvent member, bool join)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            var welcome = scope.ServiceProvider.GetRequiredService<IWelcomeService>();

            WelcomePlan plan = join
                ? await welcome.OnJoinAsync(member).ConfigureAwait(false)
                : await welcome.OnLeaveAsync(member).ConfigureAwait(false);

            if (plan.IsNoop)
            {
                return;
            }

            if (plan.AutoroleId is { } roleId)
            {
                await GrantAsync(guild, member.UserId, roleId).ConfigureAwait(false);
            }

            if (plan.ChannelId is { } channelId)
            {
                await PostAsync(guild, channelId, member, plan, join).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "WelcomeFlow failed for {UserId} in {GuildId}", member.UserId, member.GuildId);
        }
    }

    private async Task GrantAsync(SocketGuild guild, ulong userId, ulong roleId)
    {
        SocketGuildUser? member = guild.GetUser(userId);
        SocketRole? role = guild.GetRole(roleId);

        if (member is null || role is null)
        {
            log.LogWarning(
                "Autorole {RoleId} not granted in {GuildId} — role or member is gone", roleId, guild.Id);
            return;
        }

        // Discord refuses a role at or above Sonarr's own top role. Checking first keeps the log a
        // warning instead of an exception on every single join.
        SocketGuildUser? self = guild.CurrentUser;
        if (self is null || !self.GuildPermissions.ManageRoles || role.Position >= TopRole(self))
        {
            log.LogWarning(
                "Autorole {RoleId} in {GuildId} is above Sonarr or Manage Roles is missing", roleId, guild.Id);
            return;
        }

        await member.AddRoleAsync(role, new RequestOptions { AuditLogReason = "Autorole on join" })
            .ConfigureAwait(false);
    }

    private async Task PostAsync(
        SocketGuild guild, ulong channelId, MemberEvent member, WelcomePlan plan, bool join)
    {
        if (guild.GetTextChannel(channelId) is not { } channel)
        {
            log.LogWarning(
                "Welcome channel {ChannelId} in {GuildId} is gone or invisible", channelId, guild.Id);
            return;
        }

        Embed embed = new EmbedBuilder()
            .WithTitle(plan.Title)
            .WithDescription(plan.Body)
            .WithColor(join ? new Color(0x57F287) : new Color(0x99AAB5))
            .WithThumbnailUrl(guild.GetUser(member.UserId)?.GetDisplayAvatarUrl(size: 128))
            .WithFooter(member.MemberCount is { } count ? $"Members: {count}" : null)
            .WithCurrentTimestamp()
            .Build();

        await channel.SendMessageAsync(
            embed: embed,
            allowedMentions: AllowedMentions.None)
            .ConfigureAwait(false);
    }

    private static MemberEvent Snapshot(SocketGuild guild, IUser user)
        => new(
            guild.Id,
            user.Id,
            (user as IGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username,
            user.CreatedAt,
            guild.MemberCount);

    private static int TopRole(SocketGuildUser member)
        => member.Roles.Count == 0 ? 0 : member.Roles.Max(r => r.Position);
}
