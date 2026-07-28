using Sonarr.Domain.Web;

namespace Sonarr.Application.Tests.Web;

/// <summary>
/// The login arithmetic (docs/09). Pure statics, so these run without a web server or a database.
/// </summary>
public class WebAuthRulesTests
{
    [Fact]
    public void Token_avoids_ambiguous_characters()
    {
        // 0/O and 1/I in a code read aloud from a DM is a support ticket per login.
        Assert.DoesNotContain('0', WebAuthRules.Alphabet);
        Assert.DoesNotContain('O', WebAuthRules.Alphabet);
        Assert.DoesNotContain('1', WebAuthRules.Alphabet);
        Assert.DoesNotContain('I', WebAuthRules.Alphabet);
    }

    [Fact]
    public void Token_is_eight_chars_from_the_alphabet()
    {
        for (int i = 0; i < 50; i++)
        {
            string token = WebAuthRules.NewToken();
            Assert.Equal(WebAuthRules.TokenLength, token.Length);
            Assert.All(token, c => Assert.Contains(c, WebAuthRules.Alphabet));
        }
    }

    [Fact]
    public void Session_id_is_256_bits_of_hex()
    {
        string id = WebAuthRules.NewSessionId();

        Assert.Equal(64, id.Length);
        Assert.NotEqual(id, WebAuthRules.NewSessionId());
    }

    [Fact]
    public void Hash_is_stable_and_not_the_input()
    {
        string hash = WebAuthRules.Hash("ABCD2345");

        Assert.Equal(hash, WebAuthRules.Hash("ABCD2345"));
        Assert.NotEqual("ABCD2345", hash);
        Assert.Equal(64, hash.Length);
    }

    [Theory]
    [InlineData("abcd2345", "ABCD2345")]
    [InlineData("ABCD-2345", "ABCD2345")]
    [InlineData("abcd_2345", "ABCD2345")]
    [InlineData(" ABCD 2345 ", "ABCD2345")]
    public void Normalize_forgives_how_people_retype_a_code(string typed, string expected) =>
        Assert.Equal(expected, WebAuthRules.Normalize(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD")]
    [InlineData("ABCD23456")]
    [InlineData("ABCD01OI")]      // characters not in the alphabet
    [InlineData("ABCD 23 45 6")]
    public void Normalize_rejects_anything_that_is_not_a_code(string? typed) =>
        Assert.Null(WebAuthRules.Normalize(typed));

    [Theory]
    [InlineData("@someone", "someone")]
    [InlineData("  Someone  ", "Someone")]
    public void NormalizeUsername_strips_the_at_and_the_whitespace(string typed, string expected) =>
        Assert.Equal(expected, WebAuthRules.NormalizeUsername(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    public void NormalizeUsername_rejects_empty(string? typed) =>
        Assert.Null(WebAuthRules.NormalizeUsername(typed));

    [Fact]
    public void NormalizeUsername_rejects_something_no_handle_could_be()
    {
        // Trust boundary: the value goes into a LIKE-free equality query, but a megabyte of
        // "username" is still a megabyte we should not carry.
        Assert.Null(WebAuthRules.NormalizeUsername(new string('a', 65)));
    }

    [Fact]
    public void Format_splits_the_code_for_reading()
    {
        Assert.Equal("ABCD-2345", WebAuthRules.Format("ABCD2345"));
        Assert.Equal("short", WebAuthRules.Format("short"));
    }

    [Fact]
    public void Session_expiry_is_24_hours_or_90_days()
    {
        DateTimeOffset now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddHours(24), WebAuthRules.SessionExpiry(remember: false, now));
        Assert.Equal(now.AddDays(90), WebAuthRules.SessionExpiry(remember: true, now));
    }

    [Fact]
    public void Remember_me_is_recoverable_from_the_window()
    {
        DateTimeOffset now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.False(WebAuthRules.IsRemembered(now, WebAuthRules.SessionExpiry(false, now)));
        Assert.True(WebAuthRules.IsRemembered(now, WebAuthRules.SessionExpiry(true, now)));
    }

    [Fact]
    public void Renewal_only_kicks_in_past_the_half_life()
    {
        DateTimeOffset created = new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset expires = created.AddHours(24);

        // A page view in the first half is not worth a row write.
        Assert.Null(WebAuthRules.Renewal(created, expires, created.AddHours(1)));

        DateTimeOffset? renewed = WebAuthRules.Renewal(created, expires, created.AddHours(20));
        Assert.NotNull(renewed);
        Assert.True(renewed > expires);
    }

    [Fact]
    public void Renewal_does_not_resurrect_an_expired_session()
    {
        DateTimeOffset created = new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset expires = created.AddHours(24);

        Assert.Null(WebAuthRules.Renewal(created, expires, expires.AddMinutes(1)));
    }

    [Fact]
    public void IsUsable_wants_unused_and_unexpired()
    {
        DateTimeOffset now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        Assert.True(WebAuthRules.IsUsable(used: false, now.AddMinutes(5), now));
        Assert.False(WebAuthRules.IsUsable(used: true, now.AddMinutes(5), now));
        Assert.False(WebAuthRules.IsUsable(used: false, now.AddMinutes(-1), now));
    }
}
