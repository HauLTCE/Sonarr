using Sonarr.Elaine.Matching;

namespace Sonarr.Elaine.Tests;

public class NormalizerTests
{
    [Fact]
    public void CasedAndLower_AreAlwaysIndexAligned()
    {
        // The whole capture mechanism rests on this: regex spans are found on Lower and
        // sliced out of Cased, so a single length mismatch would corrupt every capture.
        string[] inputs =
        [
            "My Name Is Sam",
            "İstanbul ﬁsh ǅungla",
            "STRASSE ß ẛ",
            "emoji 🙂 and 𝔘𝔫𝔦𝔠𝔬𝔡𝔢",
            "  curly “quotes” and — dashes  ",
        ];

        foreach (string input in inputs)
        {
            Normalized n = Normalizer.Normalize(input);
            Assert.Equal(n.Cased.Length, n.Lower.Length);
        }
    }

    [Fact]
    public void CapturesComeBackWithOriginalCasing()
    {
        Normalized n = Normalizer.Normalize("My Name Is Sam");
        int at = n.Lower.IndexOf("sam", StringComparison.Ordinal);
        Assert.Equal("Sam", n.Cased.Substring(at, 3));
    }

    [Theory]
    [InlineData("  hello   there  ", "hello there")]
    [InlineData("hello there!!!", "hello there")]
    [InlineData("what?", "what")]
    [InlineData("“quoted”", "\"quoted\"")]
    [InlineData("em—dash", "em-dash")]
    public void Collapse_TrimsAndFoldsPunctuation(string input, string expected) =>
        Assert.Equal(expected, Normalizer.Normalize(input).Cased);

    [Fact]
    public void Tokens_KeepInteriorApostrophesAndDropEdgePunctuation()
    {
        Normalized n = Normalizer.Normalize("don't (really) stop, ok");
        Assert.Equal(["don't", "really", "stop", "ok"], n.Tokens);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("...", true)]
    [InlineData("hi", false)]
    public void EmptyStyle_CoversWhitespaceAndPurePunctuation(string input, bool empty) =>
        Assert.Equal(empty, Normalizer.Normalize(input).Has(TextStyle.Empty));

    [Theory]
    [InlineData("STOP IGNORING ME", true)]
    [InlineData("stop ignoring me", false)]
    [InlineData("OK", false)] // too short to be shouting; "OK" is just how people type it
    [InlineData("Hello There Friend", false)]
    public void Caps_NeedsEnoughLettersAndAHighRatio(string input, bool caps) =>
        Assert.Equal(caps, Normalizer.Normalize(input).Has(TextStyle.Caps));

    [Fact]
    public void WallOfText_TripsOnLength()
    {
        Assert.True(Normalizer.Normalize(new string('a', Normalizer.WallOfTextChars))
            .Has(TextStyle.WallOfText));
        Assert.False(Normalizer.Normalize(new string('a', Normalizer.WallOfTextChars - 1))
            .Has(TextStyle.WallOfText));
    }

    [Fact]
    public void EmojiFlood_CountsSurrogatePairsOnce()
    {
        Normalized three = Normalizer.Normalize("🙂🙂🙂");
        Assert.Equal(3, three.EmojiCount);
        Assert.False(three.Has(TextStyle.EmojiFlood));

        Normalized four = Normalizer.Normalize("🙂🙂🙂🙂");
        Assert.Equal(Normalizer.EmojiFloodCount, four.EmojiCount);
        Assert.True(four.Has(TextStyle.EmojiFlood));
    }

    [Theory]
    [InlineData("noooo way", true)]
    [InlineData("no way", false)]
    [InlineData("=)))))))", true)]
    [InlineData("hmmm", true)]
    // A repeated digit is a value, not stretched typing. The "000" here made a circuits question
    // elongated, and she answered a homework problem with "that's a lot of extra letters."
    [InlineData("resistors are all 1000 ohms, find E as Vcc is 12V", false)]
    [InlineData("i have 3000 of them", false)]
    public void Elongated_NeedsThreeInARow(string input, bool elongated) =>
        Assert.Equal(elongated, Normalizer.Normalize(input).Has(TextStyle.Elongated));

    [Fact]
    public void CustomEmoji_IsNotWordsButIsStillContent()
    {
        // A snowflake is 18 digits, so it repeats: this one ends in "555", which tripped the
        // elongation detector and drew "stretching it out doesn't make it interesting." at a
        // message with no padding in it. The name and id are markup, not words anyone typed --
        // so they are gone before any detector sees the text, not just the elongation one.
        Normalized alone = Normalizer.Normalize("<:pinecone_dumb:1492441226148843560>");

        Assert.False(alone.Has(TextStyle.Elongated));
        Assert.Empty(alone.Tokens);

        // Not empty, which is the half this test used to assert the other way around. Deleting the
        // emoji outright left a reaction-only message with no text at all, so it took ChatEngine's
        // empty path and came back "nothing to say? then don't speak." -- five corpus rows where
        // she told someone who had sent her an emoji that they had sent her nothing. It folds to
        // one U+FFFC now, whose category is So, so it counts as an emoji and reaches emoji_react.
        Assert.False(alone.Has(TextStyle.Empty));
        Assert.Equal(1, alone.EmojiCount);

        // Four of them flood exactly as four native emoji do -- the placeholder is what makes the
        // count work, and the count is what EMOJI_ONLY matches on.
        Normalized flood = Normalizer.Normalize(
            "<:a:1492441226148843560><:b:1492441226148843561>"
            + "<:c:1492441226148843562><:d:1492441226148843563>");

        Assert.Equal(Normalizer.EmojiFloodCount, flood.EmojiCount);
        Assert.True(flood.Has(TextStyle.EmojiFlood));

        // Animated variant, and the surrounding words survive as words -- the placeholder is
        // trimmed off the tokens, so no keyword list ever sees a token it cannot match.
        Normalized inline = Normalizer.Normalize("nice <a:kekw:1492441226148843560> shot");

        Assert.False(inline.Has(TextStyle.Elongated));
        Assert.Equal(["nice", "shot"], inline.Tokens);
    }

    [Theory]
    [InlineData("look at https://example.com", TextStyle.Link)]
    [InlineData("check www.example.org", TextStyle.Link)]
    [InlineData("hey <@1234567890>", TextStyle.Mention)]
    [InlineData("hey @everyone", TextStyle.Mention)]
    public void LinksAndMentions_AreDetected(string input, TextStyle expected) =>
        Assert.True(Normalizer.Normalize(input).Has(expected));

    [Fact]
    public void Normalize_IsPure()
    {
        // Compared field by field on purpose: Tokens is a List, so the compiler-generated
        // record equality would compare list references, not contents, and pass vacuously.
        const string Input = "Hey THERE!! 🙂🙂🙂🙂 https://x.com noooo";
        Normalized first = Normalizer.Normalize(Input);
        Normalized second = Normalizer.Normalize(Input);

        Assert.Equal(first.Cased, second.Cased);
        Assert.Equal(first.Lower, second.Lower);
        Assert.Equal(first.Tokens, second.Tokens);
        Assert.Equal(first.Style, second.Style);
        Assert.Equal(first.EmojiCount, second.EmojiCount);
    }

    [Fact]
    public void NullInput_IsEmptyNotAnException()
    {
        Normalized n = Normalizer.Normalize(null);
        Assert.True(n.Has(TextStyle.Empty));
        Assert.Empty(n.Tokens);
    }
}
