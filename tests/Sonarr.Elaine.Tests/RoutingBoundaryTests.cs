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

    // ----------------------------------------- disruptive vocabulary vs the insult wordlists

    [Fact]
    public void CringeAsContentDrawsTheCringePool() =>
        Assert.Equal("DISRUPTIVE_CRINGE", Reply("that edit was cringeworthy").IntentId);

    [Fact]
    public void BareCringeStaysAnInsult() =>
        Assert.Equal("INSULT_EXTRA", Reply("cringe").IntentId);

    [Fact]
    public void NpcBehaviourDrawsTheNpcPool() =>
        Assert.Equal("DISRUPTIVE_NPC", Reply("npc moment").IntentId);

    [Fact]
    public void BareNpcStillOpensTheArgument() =>
        Assert.Equal("INSULT_OPEN", Reply("npc").IntentId);

    [Fact]
    public void DeluluTalkGetsTheRealityCheck() =>
        Assert.Equal("DISRUPTIVE_DELULU", Reply("delulu is the solulu").IntentId);

    [Fact]
    public void MainCharacterAnnouncementsDrawTheirOwnPool() =>
        Assert.Equal("DISRUPTIVE_MAIN_CHARACTER", Reply("i'm the main character").IntentId);

    [Fact]
    public void RagebaitGetsNamedAsRagebait() =>
        Assert.Equal("DISRUPTIVE_RAGEBAIT", Reply("hot take: bots are people").IntentId);

    [Fact]
    public void UnpopularOpinionsAboutSomethingStayOpinionQuestions() =>
        Assert.Equal("Q_OPINION", Reply("unpopular opinion on pineapple pizza").IntentId);

    [Fact]
    public void AnnouncingYouAlreadySaidItDrawsTheRepetitionPool() =>
        Assert.Equal("DISRUPTIVE_REPETITION", Reply("like i said, it doesn't work").IntentId);

    [Fact]
    public void ForcedPositivityGetsToldOff() =>
        Assert.Equal("DISRUPTIVE_TOXIC_POSITIVITY", Reply("good vibes only").IntentId);

    [Fact]
    public void TheDrinkCalledTeaStaysWithDrinks() =>
        Assert.Equal("DRINKS", Reply("i want some iced tea").IntentId);

    // ------------------------------------------- user-behaviour labels vs the wired routes

    [Fact]
    public void OrderingHerAroundDrawsTheDemandingPool() =>
        Assert.Equal("USER_DEMANDING", Reply("hurry up").IntentId);

    [Theory]
    [InlineData("am i pretty")]
    [InlineData("fishing for compliments")]
    public void IndirectComplimentFishingDrawsTheFishingPool(string text) =>
        Assert.Equal("USER_FISHING", Reply(text).IntentId);

    [Fact]
    public void ExplicitComplimentRequestsStayWithTheGameRoute() =>
        Assert.Equal("COMPLIMENT_REQ", Reply("tell me i'm pretty").IntentId);

    [Fact]
    public void RatingRequestsStayWithTheGameRoute() =>
        Assert.Equal("RATE", Reply("rate me").IntentId);

    [Fact]
    public void ThirdPartyProvocationDrawsTheInstigatingPool() =>
        Assert.Equal("USER_INSTIGATING", Reply("they said you're weird").IntentId);

    [Fact]
    public void BeingCalledALiarIsNotDisagreement() =>
        Assert.Equal("USER_LYING", Reply("you're lying").IntentId);

    [Fact]
    public void BeingToldSheIsWrongStaysDisagreement() =>
        Assert.Equal("DISAGREE", Reply("you're wrong").IntentId);

    [Fact]
    public void SpirallingDrawsTheOverthinkingPool() =>
        Assert.Equal("USER_OVERTHINKING", Reply("i'm overthinking this").IntentId);

    [Fact]
    public void TheClassicDismissalsReadAsPassiveAggressive() =>
        Assert.Equal("USER_PASSIVE_AGGRESSIVE", Reply("whatever you say").IntentId);

    [Fact]
    public void ASighedBreathReadsAsRelief() =>
        Assert.Equal("USER_RELIEF", Reply("phew").IntentId);

    [Fact]
    public void SimpingVocabularyDrawsTheSimpingPool() =>
        Assert.Equal("USER_SIMPING", Reply("i'm a simp for her").IntentId);

    [Fact]
    public void TraumaTalkGetsTheBoundary() =>
        Assert.Equal("USER_TRAUMA_DUMP", Reply("let me tell you about my trauma").IntentId);

    [Fact]
    public void WthIsUnclearToHerToo() =>
        Assert.Equal("USER_UNCLEAR", Reply("wth").IntentId);

    [Fact]
    public void WtfStaysAComplaintAboutHerOutput() =>
        Assert.Equal("BOT_DERISION", Reply("wtf").IntentId);

    [Fact]
    public void DramaQueenDrawsTheDramaPool() =>
        Assert.Equal("USER_DRAMA", Reply("you're such a drama queen").IntentId);

    [Fact]
    public void BareDramaStaysWithGossip() =>
        Assert.Equal("GOSSIP", Reply("drama").IntentId);

    // --------------------------------------- specific refusals vs the flat request route

    [Theory]
    [InlineData("google that")]
    [InlineData("search this for me")]
    public void LookupOrdersDrawTheSearchPool(string text) =>
        Assert.Equal("REQUEST_SEARCH", Reply(text).IntentId);

    [Fact]
    public void ALookupQuestionStaysWithTheCatchAll() =>
        Assert.Equal("QUESTION_IN", Reply("what is the capital of france").IntentId);

    [Fact]
    public void RandomFactRequestsDrawTheRandomPool() =>
        Assert.Equal("REQUEST_RANDOM", Reply("tell me a random fact").IntentId);

    [Fact]
    public void RelayingToAThirdPartyDrawsTheRelayPool() =>
        Assert.Equal("REQUEST_RELAY", Reply("tell them i said hi").IntentId);

    [Fact]
    public void AskingHerForAJokeStaysWithTheFlatRoute() =>
        Assert.Equal("REQUEST", Reply("tell me a joke").IntentId);

    [Fact]
    public void AskingForPicturesDrawsTheSelfiePool() =>
        Assert.Equal("REQUEST_SELFIE", Reply("send a pic").IntentId);

    [Fact]
    public void AskingForAFriendDrawsTheThirdPartyPool() =>
        Assert.Equal("REQUEST_FOR_OTHERS", Reply("asking for a friend").IntentId);

    [Fact]
    public void BeingHappyForSomeoneIsNotAFavor() =>
        Assert.Equal("HAPPY", Reply("i'm happy for them").IntentId);

    [Theory]
    [InlineData("do my homework")]
    [InlineData("make me a sandwich")]
    public void ImperativeTasksDrawTheTaskRefusalPool(string text) =>
        Assert.Equal("REQUEST_ACTION", Reply(text).IntentId);

    [Fact]
    public void TellingHerSheMakesYouHappyIsAffectionNotGuiltOrATask() =>
        Assert.Equal("AFFECTION", Reply("you make me happy").IntentId);

    [Fact]
    public void BlameStillReadsAsGuilt() =>
        Assert.Equal("USER_GUILT", Reply("you made me cry").IntentId);

    [Fact]
    public void ProposalsDrawTheProposalPool() =>
        Assert.Equal("REQUEST_MARRIAGE", Reply("let's get married").IntentId);

    [Fact]
    public void ClaimingTheMarriageAlreadyExistsStaysADelusion() =>
        Assert.Equal("MARRIAGE_DELUSION", Reply("we're married").IntentId);

    // ------------------------------------------------ reporting a malfunction vs bot tests

    [Fact]
    public void ReportingHerMalfunctionDrawsTheErrorPool() =>
        Assert.Equal("BOT_BROKEN", Reply("you're glitching").IntentId);

    [Fact]
    public void CheckingWhetherSheIsUpStaysABotTest() =>
        Assert.Equal("BOT_TEST", Reply("are you working").IntentId);
}
