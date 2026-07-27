using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sonarr.Domain.Abstractions;
using Sonarr.Domain.Caching;
using Sonarr.Domain.Moderation;

namespace Sonarr.Application.Moderation;

/// <inheritdoc cref="IAntiSpamService"/>
public sealed partial class AntiSpamService(
    IModerationService moderation,
    ICooldownStore cooldowns,
    ILogger<AntiSpamService> log) : IAntiSpamService
{
    /// <summary>
    /// discord.gg / discord.com/invite / discordapp.com/invite, plus the .me and .li mirrors.
    /// Compiled at build time by the regex generator — no runtime codegen on a J2900.
    /// </summary>
    [GeneratedRegex(
        @"(?:discord(?:app)?\.com/invite|discord\.gg|discord\.me|discord\.li)/[a-z0-9\-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InviteLink();

    public async Task<SpamVerdict> EvaluateAsync(SpamCandidate message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Exemptions first: they are the cheapest checks and cover bots, webhooks and staff.
        if (message.AuthorIsBot || message.AuthorIsModerator)
        {
            return SpamVerdict.Clean;
        }

        ModerationPolicy policy = await moderation.GetPolicyAsync(message.GuildId, ct);
        if (!policy.AntiSpamEnabled)
        {
            return SpamVerdict.Clean;
        }

        // Content-only rules next — no I/O, and they catch the loudest abuse.
        var mentions = message.MentionedUserCount + message.MentionedRoleCount;
        if (message.MentionsEveryone || mentions >= policy.MassMentionThreshold)
        {
            return Verdict(
                policy,
                SpamTrigger.MassMention,
                message.MentionsEveryone
                    ? "Anti-spam: @everyone/@here from a non-staff member"
                    : $"Anti-spam: {mentions} mentions in one message",
                new Dictionary<string, string>
                {
                    ["mentions"] = mentions.ToString(CultureInfo.InvariantCulture),
                    ["threshold"] = policy.MassMentionThreshold.ToString(CultureInfo.InvariantCulture),
                });
        }

        if (policy.InviteLinksBlocked && InviteLink().IsMatch(message.Content))
        {
            return Verdict(
                policy,
                SpamTrigger.InviteLink,
                "Anti-spam: invite link",
                new Dictionary<string, string> { ["rule"] = "invite_link" });
        }

        // Identical-flood needs the rl:spam window. Empty content (attachment-only) is not a
        // flood candidate — hashing "" would collapse every image post into one bucket.
        var normalized = Normalize(message.Content);
        if (normalized.Length == 0)
        {
            return SpamVerdict.Clean;
        }

        var seen = await cooldowns.RecordMessageHashAsync(
            message.GuildId, message.AuthorId, Hash(normalized), ct);

        if (seen < policy.IdenticalFloodThreshold)
        {
            return SpamVerdict.Clean;
        }

        // The window is per-user; clear it so the same burst does not fire on every later
        // message while the mod action is still landing.
        await cooldowns.ClearMessageHashesAsync(message.GuildId, message.AuthorId, ct);

        return Verdict(
            policy,
            SpamTrigger.IdenticalFlood,
            $"Anti-spam: same message {seen} times in a row",
            new Dictionary<string, string>
            {
                ["repeats"] = seen.ToString(CultureInfo.InvariantCulture),
                ["threshold"] = policy.IdenticalFloodThreshold.ToString(CultureInfo.InvariantCulture),
            });
    }

    private SpamVerdict Verdict(
        ModerationPolicy policy,
        SpamTrigger trigger,
        string reason,
        Dictionary<string, string> detail)
    {
        // Counts and rule names only — the message itself is never logged (docs/06, hard rule 1).
        log.LogInformation("AntiSpam {Trigger} → {Action} ({Detail})",
            trigger, policy.Action, string.Join(" ", detail.Select(d => $"{d.Key}={d.Value}")));

        return new SpamVerdict(
            trigger,
            policy.Action,
            reason,
            policy.DeleteOffendingMessage,
            policy.Action is SpamAction.Timeout ? policy.Timeout : null,
            detail);
    }

    /// <summary>
    /// Case/whitespace-insensitive so "STOP" and "s t o p" collapse onto one bucket, which is
    /// what a flooder actually does to dodge a naive comparison.
    /// </summary>
    private static string Normalize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(content.Length);
        foreach (var ch in content)
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A short hash is the Redis field name — the content never leaves this method, so the
    /// spam state holds no message text (docs/06-data-and-privacy.md).
    /// </summary>
    private static string Hash(string normalized)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(normalized), digest);

        // 8 bytes is plenty to separate a handful of messages inside a 5 min per-user window,
        // and keeps the hash field small.
        return Convert.ToHexStringLower(digest[..8]);
    }
}
