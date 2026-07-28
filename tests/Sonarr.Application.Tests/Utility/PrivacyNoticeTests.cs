using Sonarr.Domain.Utility;

namespace Sonarr.Application.Tests.Utility;

/// <summary>
/// <c>/privacy</c> has to agree with docs/06-data-and-privacy.md, so these read the doc and check
/// the notice against it. A doc edit that the command doesn't follow fails the build.
/// </summary>
public sealed class PrivacyNoticeTests
{
    /// <summary>
    /// Null where docs/ is not on disk. The folder is gitignored, so CI checks out a tree without
    /// it — an eager read there threw in the type initializer and took the seven tests that never
    /// touch the doc down with it. The two doc-coupling checks return early instead: they are a
    /// dev-time guard against editing the doc without the command, and the dev has the doc.
    /// </summary>
    private static readonly string? Doc = ReadDoc();

    [Fact]
    public void Every_hard_rule_in_the_doc_is_in_the_notice()
    {
        var rules = string.Join(" ", PrivacyNotice.Rules);

        // The five hard rules of docs/06, matched on their load-bearing phrases rather than whole
        // sentences (the notice is user-facing wording, the doc is prose).
        Assert.Contains("No message-content logging", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addressed to me", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DM content is never stored", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-way", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No third parties", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_doc_still_states_the_rules_the_notice_promises()
    {
        if (Doc is null)
        {
            return;
        }

        Assert.Contains("no message-content logging", Doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("third part", Doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-way", Doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retention_numbers_match_the_doc()
    {
        var retention = string.Join(" ", PrivacyNotice.Retention);

        Assert.Contains("200", retention, StringComparison.Ordinal);
        Assert.Contains("400 days", retention, StringComparison.Ordinal);
        Assert.Contains("10 minutes", retention, StringComparison.Ordinal);

        if (Doc is null)
        {
            return;
        }

        // The same figures have to be the ones in the doc.
        Assert.Contains("400 days", Doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("200", Doc, StringComparison.Ordinal);
    }

    [Fact]
    public void What_cannot_be_deleted_is_stated_plainly()
    {
        var rights = string.Join(" ", PrivacyNotice.Rights);

        Assert.Contains("Not deletable", rights, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moderation cases", rights, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aggregate", rights, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_notice_covers_every_category_the_doc_collects()
    {
        var collected = string.Join(" ", PrivacyNotice.Collected);

        foreach (var category in new[]
        {
            "timezone", "birthday", "moderation", "reminder", "voice", "xp", "music", "session",
        })
        {
            Assert.Contains(category, collected, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Renders_with_the_panel_link_when_a_url_is_configured()
    {
        var body = PrivacyNotice.Render("https://sonarr.example.com/");

        Assert.Contains("https://sonarr.example.com/me/data", body, StringComparison.Ordinal);
        Assert.DoesNotContain("//me/data", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_without_a_broken_link_when_no_url_is_configured()
    {
        var body = PrivacyNotice.Render(null);

        Assert.DoesNotContain("/me/data", body, StringComparison.Ordinal);
        Assert.Contains("What I keep about you", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Fits_in_an_embed_description()
    {
        // 4096 is Discord's embed description limit; the notice is too long for a plain message
        // (2000), which is why PrivacyModule uses an embed.
        var body = PrivacyNotice.Render("https://sonarr.example.com");

        Assert.True(body.Length <= 4096, $"the notice is {body.Length} characters");
    }

    [Fact]
    public void Never_calls_itself_by_the_internal_persona_name()
    {
        // Hard rule: users only ever see "Sonarr".
        var body = PrivacyNotice.Render("https://sonarr.example.com");

        Assert.DoesNotContain("elaine", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadDoc()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sonarr.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory.FullName, "docs", "06-data-and-privacy.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
