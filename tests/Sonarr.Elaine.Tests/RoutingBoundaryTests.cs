using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// Pins the boundaries of routes that were carved out of, or added beside, an existing intent.
/// Each pair proves both halves: the row that moved, and the row that must stay put.
/// </summary>
/// <remarks>
/// The split-roast and mixed-compound routes replace first-come matching with scoring, so the
/// only thing keeping "roast me" out of roast_target is the score model — these tests are what
/// CI fails on if a pattern edit ever lets the two halves bleed into each other.
/// </remarks>
public class RoutingBoundaryTests
{
    private static PersonaGraph Graph => SeedPersona.Graph;

    private static ChatEngine Engine => new(Graph);

    private static TurnResult Reply(string text) =>
        Engine.Turn(ConversationState.Fresh(Graph.Root, 7), new TurnInput { Text = text });

    // ------------------------------------------------------------ roast me vs roast them

    [Theory]
    [InlineData("roast me")]
    [InlineData("can you roast me")]
    public void RoastingYourselfStaysWithTheRequesterPool(string text) =>
        Assert.Equal("ROAST_REQ", Reply(text).IntentId);

    [Theory]
    [InlineData("roast him")]
    [InlineData("roast my friend")]
    public void RoastingSomeoneElseDrawsTheThirdPartyPool(string text) =>
        Assert.Equal("ROAST_TARGET", Reply(text).IntentId);

    // ----------------------------------------------------- comparison questions vs misnaming

    [Theory]
    [InlineData("who's better you or chatgpt")]
    [InlineData("are you smarter than siri")]
    public void BeingComparedToAnotherBotIsAComparisonNotAMisnaming(string text) =>
        Assert.Equal("QUESTION_COMPARISON", Reply(text).IntentId);

    [Fact]
    public void BareAssistantNamesStillReadAsMisnaming() =>
        Assert.Equal("WRONG_NAME_SIRI", Reply("hey siri").IntentId);

    // ------------------------------------------------------------------ told jokes vs laughter

    [Theory]
    [InlineData("why did the chicken cross the road")]
    [InlineData("what do you call a fish with no eyes")]
    public void TellingAJokeGetsTheBadJokeReaction(string text) =>
        Assert.Equal("BAD_JOKE", Reply(text).IntentId);

    // ------------------------------------------------------------ compound insult/affection lines

    [Theory]
    [InlineData("why are you so stupid")]
    [InlineData("what is wrong with you")]
    public void AQuestionWrappedInAnInsultBeatsTheBareJab(string text) =>
        Assert.Equal("MIXED_QUESTION_INSULT", Reply(text).IntentId);

    [Fact]
    public void ABareInsultStillOpensTheArgument() =>
        Assert.Equal("INSULT_OPEN", Reply("you're stupid").IntentId);

    [Fact]
    public void AQuestionWrappedInAffectionIsNotALookup() =>
        Assert.Equal("MIXED_QUESTION_AFFECTION", Reply("why are you so perfect").IntentId);

    [Fact]
    public void AnInsultFollowedByAffectionReadsTheWhiplash() =>
        Assert.Equal("MIXED_INSULT_AFFECTION", Reply("you're annoying but i like you anyway").IntentId);

    [Theory]
    [InlineData("you're not stupid")]
    [InlineData("you're actually smart")]
    [InlineData("i like you even though you're annoying")]
    public void BackhandedComplimentsDoNotScoreAsInsults(string text) =>
        Assert.Equal("MIXED_AFFECTION_INSULT", Reply(text).IntentId);

    // --------------------------------------------------------------- meals vs the flat food route

    [Theory]
    [InlineData("i skipped breakfast this morning", "TOPIC_BREAKFAST")]
    [InlineData("lunch time", "TOPIC_LUNCH")]
    [InlineData("i am eating dinner", "TOPIC_DINNER")]
    public void NamedMealsDrawTheirOwnPool(string text, string expected) =>
        Assert.Equal(expected, Reply(text).IntentId);

    [Theory]
    [InlineData("what should i eat")]
    [InlineData("pizza time")]
    public void EverythingElseAboutFoodStaysOnTheFlatRoute(string text) =>
        Assert.Equal("FOOD", Reply(text).IntentId);

    [Fact]
    public void DeclaringLoveForAFoodIsAPreferenceNotFoodTalk() =>
        Assert.Equal("PREFERENCE", Reply("i love pizza").IntentId);

    // --------------------------------------------------------- arknights vs the flat gacha route

    [Theory]
    [InlineData("arknights is the best game")]
    [InlineData("amiya is best girl")]
    public void ArknightsVocabularyDrawsTheSpecificPool(string text) =>
        Assert.Equal("TOPIC_GACHA_ARKNIGHTS", Reply(text).IntentId);

    [Fact]
    public void BareGachaTalkStaysOnTheGeneralRoute() =>
        Assert.Equal("ANIME", Reply("gacha games are a money pit").IntentId);

    // ----------------------------------------- leaks vs the tea idiom vs the drink called tea

    [Fact]
    public void LeakAndRumorTalkRoutesToTheLeaksPool() =>
        Assert.Equal("TOPIC_LEAKS", Reply("any leaks about the new update").IntentId);

    [Fact]
    public void SpillingTeaStaysWithTheGossipRoute() =>
        Assert.Equal("GOSSIP", Reply("spill the tea").IntentId);

    [Fact]
    public void TheDrinkCalledTeaStaysWithDrinks() =>
        Assert.Equal("DRINKS", Reply("i want some iced tea").IntentId);
}
