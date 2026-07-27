using Sonarr.Domain.Entities.Chat;
using Sonarr.Migrator.Import;

namespace Sonarr.Application.Tests.Migration;

/// <summary>
/// The importer's pure mapping helpers — the parts that decide what gets dropped. The SQL steps
/// need a live Postgres, so they are exercised by the dry run against a copy of the old DB
/// (docs/11 cutover), not here.
/// </summary>
public class LegacyImporterTests
{
    [Theory]
    [InlineData("123:456", 123L, 456L)]
    [InlineData("global:456", 0, 0)]        // pre-multi-guild rows cannot be attributed
    [InlineData("123", 0, 0)]              // no separator
    [InlineData("123:", 0, 0)]
    [InlineData(":456", 0, 0)]
    [InlineData("0:456", 0, 0)]            // 0 is not a snowflake
    [InlineData("", 0, 0)]
    [InlineData(null, 0, 0)]
    public void SplitsScopeKeyAndRejectsNonSnowflakes(string? key, long guild, long user)
    {
        bool ok = LegacyImporter.TrySplitScopeKey(key, out long guildId, out long userId);

        Assert.Equal(guild != 0, ok);
        Assert.Equal(guild, guildId);
        Assert.Equal(user, userId);
    }

    [Fact]
    public void MapsLegacyRegistersOntoTheNewDimensions()
    {
        (PersonRegisters registers, string tier) = LegacyImporter.MapRegisters(
            """
            {"anger": 3.5, "boredom": 2, "warmth": 7, "trust": 4.25,
             "grudge": true, "role": "nemesis", "times_insulted": 9}
            """);

        Assert.Equal(3.5, registers.Anger);
        Assert.Equal(2, registers.Boredom);
        Assert.Equal(7, registers.Fondness);
        Assert.Equal(4.25, registers.Trust);
        Assert.Equal(5, registers.Grudge); // bool → seeded scalar
        Assert.Equal("nemesis", tier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    public void UnreadableRegisterBlobBecomesNeutralStranger(string? json)
    {
        (PersonRegisters registers, string tier) = LegacyImporter.MapRegisters(json);

        Assert.Equal(0, registers.Anger);
        Assert.Equal(0, registers.Fondness);
        Assert.Equal(0, registers.Grudge);
        Assert.Equal("stranger", tier);
    }

    [Fact]
    public void HeldGrudgeSurvivesButUnsetOneDoesNot()
    {
        Assert.Equal(0, LegacyImporter.MapRegisters("""{"grudge": false}""").Registers.Grudge);
        Assert.Equal(5, LegacyImporter.MapRegisters("""{"grudge": true}""").Registers.Grudge);
    }

    [Fact]
    public void ReportTalliesWrittenRowsOnly()
    {
        ImportReport report = new();
        report.Add("levels.progress", read: 10, written: 8, skipped: 2);
        report.Add("chat.person", read: 3, written: 3, skipped: 0);
        report.AddSkipped("economy", "not migrated by design");

        Assert.Equal(11, report.TotalWritten);
        Assert.Contains("levels.progress", report.ToString());
        Assert.Contains("not migrated by design", report.ToString());
    }
}
