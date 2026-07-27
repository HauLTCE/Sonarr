using Discord.WebSocket;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Configuration;
using Sonarr.Domain.Levels;

namespace Sonarr.Bot.Discord.Levels;

/// <summary>
/// Voice XP accrual (docs/08-background-services.md — "voice XP tick: 60 s"): every minute, credit
/// one tick to everyone whose voice time counts, then roll up any level-ups.
/// </summary>
/// <remarks>
/// <para>
/// The anti-AFK rule lives in <see cref="XpRules.VoiceTimeCounts"/> and is enforced again inside
/// the service — alone in a channel or muted earns nothing. This pass walks the gateway's own
/// channel cache, so it costs no REST calls.
/// </para>
/// <para>
/// A session that Redis lost is simply not credited: the tracker is the gate, so "Redis down"
/// reads as "no voice XP", never as unlimited voice XP.
/// </para>
/// </remarks>
public sealed class VoiceXpAccrual(
    DiscordSocketClient client,
    IServiceScopeFactory scopes,
    ILogger<VoiceXpAccrual> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("VoiceXpAccrual ticking every {Seconds}s", XpRules.VoiceTick.TotalSeconds);

        using PeriodicTimer timer = new(XpRules.VoiceTick);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Voice XP tick failed");
            }
        }
    }

    /// <summary>One accrual pass. Internal so a test can drive it without the timer.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        if (client.ConnectionState != global::Discord.ConnectionState.Connected)
        {
            // Mid-reconnect the voice caches are empty; crediting from them would be a guess.
            return;
        }

        foreach (SocketGuild guild in client.Guilds)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await TickGuildAsync(guild, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One guild failing must not skip the rest of the tick.
                log.LogWarning(ex, "Voice XP tick failed for guild {GuildId}", guild.Id);
            }
        }
    }

    private async Task TickGuildAsync(SocketGuild guild, CancellationToken ct)
    {
        // Cheapest possible rejection: most guilds have nobody in voice most of the time.
        List<(SocketGuildUser Member, int Humans)> candidates = [];
        foreach (SocketVoiceChannel channel in guild.VoiceChannels)
        {
            if (channel.Id == guild.AFKChannel?.Id)
            {
                continue;
            }

            var humans = LevelsVoiceStateHandler.Humans(channel);
            if (humans < XpRules.VoiceMinimumHumans)
            {
                continue;
            }

            foreach (SocketGuildUser member in channel.ConnectedUsers)
            {
                if (!member.IsBot && !LevelsVoiceStateHandler.IsMuted(member.VoiceState))
                {
                    candidates.Add((member, humans));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        using IServiceScope scope = scopes.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureGate>();

        if (!await features.IsEnabledAsync(FeatureNames.Levels, guild.Id, ct).ConfigureAwait(false))
        {
            return;
        }

        var levels = scope.ServiceProvider.GetRequiredService<ILevelService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IVoiceSessionTracker>();
        LevelsPolicy policy = await levels.GetPolicyAsync(guild.Id, ct).ConfigureAwait(false);

        foreach ((SocketGuildUser member, var humans) in candidates)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // The tracked session is the gate: no session (never joined while Sonarr was up, or
            // Redis lost it) means no credit for this tick.
            VoiceSession? session = await tracker.GetAsync(guild.Id, member.Id, ct).ConfigureAwait(false);
            if (session is null)
            {
                continue;
            }

            XpAward award = await levels.AwardVoiceXpAsync(
                guild.Id,
                member.Id,
                session.ChannelId,
                humans,
                muted: false,
                XpRules.VoiceTick,
                ct).ConfigureAwait(false);

            if (!award.LeveledUp)
            {
                continue;
            }

            await LevelUpNotice.AnnounceAsync(
                guild, session.ChannelId, member, member.Id, award, policy, log).ConfigureAwait(false);

            if (award.RewardRoleIds.Count > 0)
            {
                await LevelUpNotice.GrantRolesAsync(guild, member, award.RewardRoleIds, log).ConfigureAwait(false);
            }
        }
    }
}
