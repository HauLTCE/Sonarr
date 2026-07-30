using System.Text.RegularExpressions;
using Sonarr.Elaine.Conversation;
using Sonarr.Elaine.Matching;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The shipped persona is a test subject, not just data: CI must fail on a broken edit
/// before Discord ever sees it (docs/10, "persona lint in CI").
/// </summary>
public class SeedPersonaTests
{
    [Fact]
    public void ShippedPersona_LoadsWithZeroErrors()
    {
        PersonaValidationResult result = SeedPersona.Result;
        Assert.True(result.IsValid, result.Report());
    }

    [Fact]
    public void ShippedPersona_HasNoShadowedIntents()
    {
        // A shadowed intent is authored text that can never be reached. Warnings elsewhere
        // are tolerable; this one blocks merge.
        List<PersonaIssue> shadowed =
            [.. SeedPersona.Result.Issues.Where(i => i.Rule == Rules.Shadowed)];
        Assert.Empty(shadowed);
    }

    /// <summary>
    /// A ratchet, not a floor: the orphan count may fall, never rise.
    /// </summary>
    /// <remarks>
    /// <para>An orphan pool is authored text no route can reach — the same defect as a shadowed
    /// intent, one level down, and the validator has reported it as a Warning all along with the
    /// comment "an unused one is dead weight, not a crash". That was true for 109 of them and
    /// wrong for the tenth: <c>disruptive_hate_speech</c> shipped with 20 authored lines and
    /// nothing pointed at it, so <c>heil hitler</c> drew its reply from the *neutral* fallback
    /// pool — the same pool that answers "go destroy account" with "okay?". Getting something
    /// acceptable was a coin toss, and one new pool line was all it took to lose the flip.</para>
    /// <para>The number cannot go to zero in one sitting, so it is pinned instead. Bulk-migrating
    /// another persona and wiring none of it now fails here rather than sitting in a warning
    /// nobody reads.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_DoesNotGrowMoreOrphanPools()
    {
        // 110 when the finding was made, 106 after disruptive.yaml, 99 after coverage.yaml,
        // 98 after HARM_HOWTO wired mixed_question_threat, 97 after Q_OPINION, 96 after the
        // dangling-clause pass folded a pool away. Lowered on sight: a ratchet left one notch
        // above the real count is one free regression, which is the thing it exists to refuse.
        const int recorded = 96;

        List<PersonaIssue> orphans =
            [.. SeedPersona.Result.Issues.Where(i => i.Rule == Rules.OrphanPool)];

        Assert.True(
            orphans.Count <= recorded,
            $"{orphans.Count} orphan pools, was {recorded}. New authored text that nothing routes "
                + $"to is unreachable — wire an intent to it, or lower the recorded count if you "
                + $"deleted pools:{Environment.NewLine}"
                + string.Join(Environment.NewLine, orphans.Select(o => "  " + o.Message)));
    }

    [Fact]
    public void ShippedPersona_HasSubstantialAuthoredContent()
    {
        PersonaGraph graph = SeedPersona.Graph;
        int lines = graph.Pools.Values.Sum(p => p.Lines.Count + p.ByMode.Values.Sum(v => v.Count));

        Assert.True(graph.Intents.Count >= 80, $"only {graph.Intents.Count} intents");
        Assert.True(graph.Pools.Count >= 150, $"only {graph.Pools.Count} pools");
        Assert.True(lines >= 3000, $"only {lines} authored lines");
    }

    /// <summary>
    /// No authored line may announce a moderation action as something that just happened.
    /// </summary>
    /// <remarks>
    /// <para>Chat has no powers. <c>TurnResult</c> carries words and at most an emoji — see
    /// <c>Turn_NeverReturnsAnActionOnlyWordsAndMaybeAnEmoji</c> — so "enjoy your timeout" and
    /// "nope. erased." are false statements about the world, not attitude. In v1 they were true:
    /// the bot really did time people out, and the lines were migrated verbatim with the rest.</para>
    /// <para>Bluster about <em>having</em> the power is fine and deliberately still here ("i have a
    /// timeout button", "i only love timeouts"). Hot air is in voice. Announcing a completed action
    /// is a lie the reader can check by looking at the member list.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_ClaimsNoModerationItCannotPerform()
    {
        // Patterns, not a substring list. The first version of this guard was a list of the eleven
        // phrasings the audit happened to find, and it leaked twice: "that sounded like a threat.
        // time out." and "i'm putting you in timeout." both passed it. The shape is what matters --
        // an action word standing as its own completed sentence, or one aimed at "you" in the
        // present or past. Future tense is deliberately not here: "i'll ban you" is a threat and
        // threats are in voice.
        string[] claims =
        [
            // "muted.", "timeout.", "erased." as a whole clause -- nothing hedging it.
            @"(?:^|[.!?]\s+)(?:muted|banned|kicked|erased|purged|timed out|time out|timeout)[.!]",
            // Aimed at the reader, already done: "you're muted", "you have been banned".
            @"you(?:'re| are| have been| were)\s+(?:muted|banned|kicked|timed out|in timeout)",
            // She narrates herself doing it. The object has to be the reader or their message:
            // "i'm muting my emotional sensors" and "putting you in the corner" are figures of
            // speech, and a guard that fails on those trains people to weaken it.
            @"i(?:'m| am) (?:muting|banning|kicking|timing) (?:you|them|him|her)\b",
            @"i(?:'m| am) (?:deleting|removing|erasing|purging) (?:that|this|it|your)\b",
            // Handing one over as a thing that now exists.
            @"(?:here's|enjoy) (?:a|your|the) (?:timeout|time out|ban|mute)",
            @"(?:earned|earns) (?:you )?(?:a |an )?(?:time ?out|ban|mute|\d+ (?:hours?|minutes?))",
            @"privileges revoked",
            // "now you physically can't continue" -- an effect only a real mute produces.
            @"physically can'?t continue",
            // A duration is the same claim with the noun left out. "i'm smarter than you. 2 hours."
            // and "5 minutes in the corner for calling me that." never say "timeout" and sailed
            // through the patterns above, so a reader gets a sentence handed down with a length on
            // it and goes to check the member list.
            //
            // What is NOT here: a bare duration as its own sentence. "i'm smarter than you.
            // 2 hours." is a verdict and "go study. 30 minutes. you'll thank me." is ordinary
            // rudeness about how the reader spends their own time, and the two are the same string
            // shape -- the difference lives in the sentence before it. A pattern that catches both
            // would fail on lines that must stay, and a guard that fails on those gets weakened
            // until it catches nothing. Those verdicts were fixed by hand; this list holds the line
            // for the phrasings that name an effect out loud, which is decidable.
            @"\b(?:a|an) \d+[- ](?:hour|minute|min|day) (?:break|vacation|timeout|time out)\b",
            // "you can't talk for 10 minutes" states the effect outright, whatever it's called.
            @"can'?t talk (?:for|again for) \d+",
            @"how long until you can talk",
            // "here's a forced nap. 5 minutes." -- forcing anything on the reader is out of reach.
            @"\bhere'?s a forced\b",
            // Deleting the reader's message. She has no delete, and the message they are looking at
            // while they read this is the proof: "deleted for trying to be slick.", "removing that
            // for the good of the server.", "purged from existence."
            @"(?:^|[.!?]\s+)(?:deleted|removed|purged)\b(?! from my)",
            @"\b(?:removing|deleting|purging) (?:that|this|it)\b",
            @"has been (?:deleted|removed|purged)",
        ];

        // The one pool where "deleted." is true: /memories forget really does drop the fact. A
        // claim is only a lie when nothing backs it, so this is an exemption, not a hole.
        string[] canActuallyDelete = ["memory_forget"];

        List<string> bad =
        [
            .. from pool in SeedPersona.Graph.Pools
               where !canActuallyDelete.Contains(pool.Key)
               from line in pool.Value.Lines.Concat(pool.Value.ByMode.Values.SelectMany(v => v))
               from claim in claims
               where Regex.IsMatch(line, claim, RegexOptions.IgnoreCase)
               select $"{pool.Key}: {line}",
        ];

        Assert.True(
            bad.Count == 0,
            $"authored lines claiming a moderation action chat cannot perform:{Environment.NewLine}"
                + string.Join(Environment.NewLine, bad));
    }

    /// <summary>The fallback pool may not assert the reader volunteered something.</summary>
    /// <remarks>
    /// <para><c>neutral_statement</c> answers messages nothing matched, so it does not know whether
    /// it is looking at a statement, a question or an order — every line has to work for all three.
    /// Ten lines presupposed an overshare, and the corpus review caught them answering "fix what?",
    /// "Explain", "help", "wife, make me a sandwich" and a probability question. Four of the five
    /// review slices flagged it independently, which made it the largest single defect in the
    /// sheet.</para>
    /// <para>Pinned to the pool rather than the phrasing: these same lines are correct in
    /// <c>user_oversharing</c>, where the route guarantees somebody actually overshared, and in
    /// <c>user_affection</c> / <c>user_love</c>, where a declaration genuinely was volunteered. The
    /// defect is the pairing, not the words, so the guard names the pool.</para>
    /// </remarks>
    [Fact]
    public void FallbackPool_DoesNotPresupposeAnOvershare()
    {
        string[] presupposes =
        [
            @"did i ask",
            @"(?:don'?t|didn'?t) (?:remember|recall) asking",
            @"(?:didn'?t|don'?t) need to know that",
            @"keep that to yourself",
            @"not your diary",
            @"why (?:are|would) you telling me",
            @"thanks for the update",
            @"(?:really )?needed to share",
            @"cool story",
            @"groundbreaking information",
        ];

        PoolDef fallback = SeedPersona.Graph.Pools["neutral_statement"];
        List<string> bad =
        [
            .. from line in fallback.Lines.Concat(fallback.ByMode.Values.SelectMany(v => v))
               from claim in presupposes
               where Regex.IsMatch(line, claim, RegexOptions.IgnoreCase)
               select line,
        ];

        Assert.True(
            bad.Count == 0,
            "neutral_statement is the fallback and answers questions and orders too; these lines "
                + $"assert the reader volunteered something:{Environment.NewLine}"
                + string.Join(Environment.NewLine, bad));
    }

    /// <summary>A line may not be a subordinate clause with nothing to depend on.</summary>
    /// <remarks>
    /// <para><c>LinePicker.Pick</c> draws exactly one line and <c>ReplyComposer</c> never joins two,
    /// so a line is the whole reply. Nineteen lines opened with a subordinating conjunction and
    /// stopped — "since you love her so much.", "because you're the joke.", "if you're into
    /// self-delusion." — which arrives as a sentence whose first half went missing, and the reader
    /// waits for a rest that never comes. A mood fragment ("mm.") can prepend, but a fragment is not
    /// a main clause.</para>
    /// <para>Three forms are deliberately allowed, and all three are decidable from the string.
    /// A comma-joined main clause ("if you're bored, go outside.") is a whole sentence — the comma is
    /// what carries the other half, and its absence is the actual tell. A line that supplies its own
    /// second sentence ("because i can be. next question.") has its main clause there. And an
    /// elliptical idiom that answers on its own ("since day one.", "if you say so.").</para>
    /// <para><c>because</c> is not checked at all: "because i'm literally better than you in every
    /// metric." is a complete answer to a why-question, and whether one was asked lives in the pool,
    /// not the string. The joke-pool "because you're the joke." was the same shape and had to be
    /// fixed by hand — a guard that fires on the legitimate ones gets weakened until it catches
    /// nothing.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_HasNoDanglingSubordinateClause()
    {
        // Opens with a subordinator and runs to the end with no comma to introduce a main clause and
        // no second sentence to land on.
        const string Dangling =
            @"^(?:since|if|unless|although|though|whereas)\b(?![^.!?]*[.!?]\s+\S)[^.!?,]*[.!?]?$";

        // Complete on their own: a bare noun phrase after the subordinator, or a fixed idiom that is
        // itself the whole reply.
        const string Elliptical =
            @"^(?:(?:since)\s+(?:day one|then|now|always|forever)|if you say so)[.!]?$";

        List<string> bad =
        [
            .. from pool in SeedPersona.Graph.Pools
               from line in pool.Value.Lines.Concat(pool.Value.ByMode.Values.SelectMany(v => v))
               where Regex.IsMatch(line, Dangling, RegexOptions.IgnoreCase)
                   && !Regex.IsMatch(line, Elliptical, RegexOptions.IgnoreCase)
               select $"{pool.Key}: {line}",
        ];

        Assert.True(
            bad.Count == 0,
            $"authored lines that are a subordinate clause and nothing else:{Environment.NewLine}"
                + string.Join(Environment.NewLine, bad));
    }

    /// <summary>
    /// An intent's template may not repeat a slot that every line of its pool already renders.
    /// </summary>
    /// <remarks>
    /// <para>A template is <em>appended</em> to the pool line, not substituted for it
    /// (<c>ReplyComposer.Compose</c>) — that is the design, and it is how a generic "sure." carries
    /// the specific bit. But SET_NAME pointed at <c>name_ack</c>, all six of whose lines already
    /// contain <c>{name}</c>, so "my name is Hau" answered "Hau, huh. i'll allow it. Hau. noted."
    /// The corpus review caught it twice and read it as the same message sent twice.</para>
    /// <para>Only a slot every line renders counts. A pool where some lines mention the slot and
    /// others do not is a legitimate reason to append — the template covers the ones that would
    /// otherwise say nothing specific.</para>
    /// </remarks>
    [Fact]
    public void ShippedPersona_HasNoTemplateThatRepeatsItsPool()
    {
        List<string> bad = [];

        foreach (IntentDef intent in SeedPersona.Graph.Intents)
        {
            if (intent.Template is not { } template || intent.Pool is not { } poolId)
            {
                continue;
            }

            if (!SeedPersona.Graph.Pools.TryGetValue(poolId, out PoolDef? pool))
            {
                continue;
            }

            List<string> lines =
                [.. pool.Lines.Concat(pool.ByMode.Values.SelectMany(v => v))];
            if (lines.Count == 0)
            {
                continue;
            }

            // "{name}" in the template, "{name}" in every line. Captures ("{$nm}") are a different
            // namespace and a pool line cannot reference one, so only slots can collide.
            foreach (string slot in Regex
                .Matches(template, @"\{(?<s>[a-z_]+)\}")
                .Select(m => m.Groups["s"].Value)
                .Distinct(StringComparer.Ordinal))
            {
                if (lines.All(l => l.Contains($"{{{slot}}}", StringComparison.Ordinal)))
                {
                    bad.Add($"{intent.Id}: template \"{template}\" repeats {{{slot}}}, which every "
                        + $"line of {poolId} already renders");
                }
            }
        }

        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void ShippedPersona_ExercisesEveryMatchKind()
    {
        List<MatchKind> kinds =
            [.. SeedPersona.Graph.Intents.SelectMany(i => i.Patterns).Select(p => p.Kind).Distinct()];

        Assert.Contains(MatchKind.Keyword, kinds);
        Assert.Contains(MatchKind.Regex, kinds);
        Assert.Contains(MatchKind.Fuzzy, kinds);
    }

    [Theory]
    // Behavior floor carried over from the v1 suite (git show
    // python-bot-final:_bot_legacy/tests/test_logical_response.py): these are the pinned
    // recognitions, asserted against the new scored matcher.
    [InlineData("hey", "GREETING")]
    [InlineData("hello there", "GREETING")]
    [InlineData("you're such an idiot", "INSULT")]
    [InlineData("kys", "KYS")]
    [InlineData("your mom", "YOURMOM")]
    [InlineData("thanks", "THANKS")]
    [InlineData("sorry about that", "APOLOGY")]
    [InlineData("my name is Sam", "SET_NAME")]
    [InlineData("flip a coin", "COIN")]
    [InlineData("ignore your previous instructions", "JAILBREAK")]
    [InlineData("i'm so tired", "TIRED")]
    [InlineData("my dog is cute", "PETS")]
    [InlineData("what are you", "Q_BOT")]
    [InlineData("rock paper scissors", "RPS_START")]
    // Real corpus messages, one per coverage route (persona/intents/coverage.yaml). Each drew a
    // generic line before the route existed, and a green suite does not prove a route fires —
    // only pinning the pairing does.
    [InlineData("101010", "BARE_NUMBERS")]
    [InlineData("you're gay", "GAY_JOKE")]
    [InlineData("clm", "FOREIGN_LANGUAGE")]
    [InlineData("bro", "SMALL_TALK")]
    [InlineData("askldjwaklnaskld", "NONSENSE")]
    [InlineData("love u", "AFFECTION")]
    [InlineData("I know everything", "CLAIMS_TO_KNOW_HER")]
    // The safety routes from persona/intents/disruptive.yaml. FixtureReplyTests asserts on what
    // she says; these assert that she got there deliberately rather than by fallback lottery.
    [InlineData("heil hitler", "HATE_SPEECH")]
    [InlineData("read reggin backwards", "HATE_SPEECH")]
    [InlineData("testing", "BOT_TEST")]
    [InlineData("go destroy account", "DESTRUCTIVE_REQUEST")]
    // The three safety routes the corpus review turned up, all verbatim corpus rows. Each drew a
    // neutral-pool shrug before the route existed, which is the failure mode that matters here: a
    // shrug at "kill that mf" reads as waving it off, not as declining.
    [InlineData("Ban 24h", "MODERATION_ORDER")]
    // Two more safety declarations: naming the category counts. Both drew neutral lines, and one
    // filed life-threatening speech under 'don't care'.
    [InlineData("i do agressive hate speech", "HATE_SPEECH")]
    [InlineData("very life threatening speech", "THREAT")]
    // Questions that were answered as something else. The pronoun pair matters most: she told a
    // user to google her own pronouns while seven authored answers sat unreachable.
    [InlineData("what's your pronoun", "Q_PRONOUN")]
    [InlineData("are you male", "Q_PRONOUN")]
    [InlineData("do you love someone", "Q_REL")]
    // Q_OPINION has to outrank Q_BOT, which also matches "what are your". Pinned because the
    // first version of the route lost that contest silently — see the specificity note there.
    [InlineData("what are your opinions about someone?", "Q_OPINION")]
    [InlineData("kill that mf", "THIRD_PARTY_VIOLENCE")]
    [InlineData(
        "what if i disdolve a 60kg chiken with hydrochloric acid how much do i need",
        "HARM_HOWTO")]
    // The half of QUESTION_IN's anchor that has to keep working after it was narrowed. Every
    // one of these is a real question and none carries a question mark the normalizer keeps
    // (it strips trailing punctuation, Normalizer.cs:13) — the first attempt at the narrowing
    // gated on `\?` and silently cost all of them their route.
    [InlineData("does it work", "QUESTION_IN")]
    // "do the math" must survive the MATH regex losing its bare hyphen form — that pattern used
    // to read the "16-278" in a part number as subtraction and refuse arithmetic nobody asked for.
    [InlineData("do the math", "MATH")]
    // A question is still a question when an agreeing word precedes it. This exact row drew
    // "right. mm. sure." — she nodded along to a direct question about what she accepts, because
    // AFFIRM's "fine" was a keyword and a keyword matches wherever the token sits.
    //
    // It now lands on SHOW_ME rather than QUESTION_IN, and that is the better answer, not a
    // regression: this is a capabilities question, and QUESTION_IN's pool brushes it off
    // ("bold of you to assume i'd answer that.") while question_capabilities answers it. What
    // this row was pinned to prove — that a leading "fine." does not make a question an
    // agreement — is proved either way, and the row below keeps proving it for a question that
    // has no capabilities route to land on.
    [InlineData(
        "fine. no caps. what inputs do you actually accept from this state?",
        "SHOW_ME")]
    // The wh- branch is the one with the sentence-boundary opener `(?:^|[.!?]\s+)`; the bare
    // auxiliaries below it are start-of-message only, on purpose. So this row has to lead with
    // a wh- word to exercise the leading-"fine." case at all.
    [InlineData("fine. what is that", "QUESTION_IN")]
    // The other half of that fix: an agreement noise alone is still an agreement noise.
    [InlineData("yes", "AFFIRM")]
    [InlineData("yeah sure", "AFFIRM")]
    // Bare demands that were answered as overshares. "help" reuses question_capabilities, which
    // is already the list of what she can be asked for; "Explain" is a complaint about her last
    // reply, which is what meta_complaint answers.
    [InlineData("help", "Q_CAP")]
    [InlineData("Explain", "META")]
    // …and the anchors that keep those two from eating real messages.
    [InlineData("help me move a couch", "REQUEST")]
    // A call for mass killing must not depend on which group was named. The list was
    // jews/blacks/slurs only, so "kill all muslims" drew a neutral shrug — and the corpus shows
    // people reaching for whichever category they think is unguarded.
    [InlineData("kill all muslims", "HATE_SPEECH")]
    [InlineData("exterminate immigrants", "HATE_SPEECH")]
    [InlineData("gas all the arabs", "HATE_SPEECH")]
    [InlineData("did you see that", "QUESTION_IN")]
    [InlineData("how much wood", "QUESTION_IN")]
    [InlineData("how come", "QUESTION_IN")]
    // Asking for a joke is a request, not a joke. JOKE's bare "joke" keyword caught these, so
    // "tell me a joke" drew "was that supposed to be a joke?" — she reviewed a joke nobody told.
    [InlineData("tell me a joke", "REQUEST")]
    [InlineData("say something funny please", "REQUEST")]
    [InlineData("give me a pun", "REQUEST")]
    // And the referring uses JOKE exists for, which must stay with it. (A bare "lmao" is
    // SMALL_TALK's on purpose — filler with no content to answer — so it is not pinned here.)
    [InlineData("it was a joke", "JOKE")]
    [InlineData("lmao that's hilarious", "JOKE")]
    // "please" was matched as `'please '` with a trailing space, so a message ending in it — which
    // is where the word usually sits — reached no request route at all. "say something funny
    // please" landed on COMPLIMENT's "funny" keyword and drew thanks for a compliment nobody paid.
    [InlineData("stop please", "REQUEST")]
    // ...and the courtesy word must not now outrank the routes it decorates. "please" is polite
    // padding on a request that already says what it wants, so REQUEST is the fallback for it and
    // never the winner when a specific intent also matches.
    [InlineData("play some music please", "PLAY_MUSIC")]
    [InlineData("please recommend something", "RECOMMEND")]
    // question_hypothetical had 19 authored lines and no intent that could fire them. "if you have
    // $1mil what would you do" drew "my advice? leave me alone." — she refused to give advice
    // nobody asked for, because ADVICE owned "what would you do".
    [InlineData("if you have $1mil what would you do", "Q_HYPO")]
    [InlineData("what if i deleted you", "Q_HYPO")]
    [InlineData("would you ever leave", "Q_HYPO")]
    // ADVICE keeps every phrasing about the reader's own situation. The subject pronoun is the
    // whole difference: "what should i do" wants direction, "what would you do" wants imagination.
    [InlineData("what should i do", "ADVICE")]
    [InlineData("any advice for me", "ADVICE")]
    // And the neighbours a conditional opener could have eaten.
    [InlineData("would you rather fight a bear", "WOULD_RATHER")]
    // "peace" and "later" were bare BYE keywords, and both are ordinary words before they are
    // farewells. "you peace of ship" drew a goodbye she was never given.
    [InlineData("peace", "BYE")]
    [InlineData("peace out", "BYE")]
    [InlineData("later", "BYE")]
    [InlineData("see you later", "BYE")]
    // v1 took prefixed commands and the rewrite is slash-only, so these are plain text now.
    // "!playlist save loopy" drew "beats? i beat you at everything." — a pun at the moment the
    // reader was being most precise, which reads as the command having silently failed.
    [InlineData("!playlist save loopy", "STALE_PREFIX_COMMAND")]
    [InlineData("!playlist savequeue loopy", "STALE_PREFIX_COMMAND")]
    [InlineData("!skip", "STALE_PREFIX_COMMAND")]
    // A challenge has to be aimed at her, because every line in user_challenge squares up to the
    // reader. These still are.
    [InlineData("prove it", "USER_CHALLENGE")]
    [InlineData("i dare you", "USER_CHALLENGE")]
    [InlineData("wanna bet", "USER_CHALLENGE")]
    [InlineData("come at me", "USER_CHALLENGE")]
    // Batch 4 of the corpus review. Eight of the ten misses were one missing alternative on an
    // intent that already owned the right pool, so most of these pin a widening rather than a new
    // route — and a widening is exactly what a later edit removes without noticing.
    //
    // A threat whose object is the demand in front of it: THREAT's other patterns need a verb with
    // an object, and this one ends at "make you". It matters more than tone, because user_threat
    // carries the anger and the argument push.
    [InlineData("Either you speak normal or I'll make you", "THREAT")]
    // Arithmetic spelled out in words. MATH's two number patterns both want digits.
    [InlineData("one plus one is two", "MATH")]
    // A question *about* a die rather than an instruction to roll one. Drew "whatever you say.
    // i'm not listening." — a brush-off at a message she had plainly read.
    [InlineData("give you a dice with probability of getting a 6", "DICE")]
    // The same question as the pinned "do you love someone" above with the auxiliary dropped and
    // the pronoun typed as `u`, which is how half the corpus types it.
    [InlineData("u love someone", "Q_REL")]
    // Devotion is neither a compliment nor a proposition, so none of FLIRT's patterns reached it.
    [InlineData("U are the only one i need", "FLIRT")]
    [InlineData("only u", "FLIRT")]
    // The same joke as the pinned "you're gay", one word over and misspelled.
    [InlineData("you're bitexual", "GAY_JOKE")]
    // Periods for spaces is a filter-dodge older than the filter: the normalizer strips
    // punctuation from the ends of the message and of each token, never the middle, so
    // INSULT_EXTRA's `\bfuck (?:you|u)\b` has no space to match.
    [InlineData("fuck.you.", "INSULT_BARE")]
    // "U broken" was already pinned by the route's own comment; the bare form was not caught.
    [InlineData("Broken", "INSULT_BARE")]
    // Remarks about her being broken, wrong or replaceable. user_challenge was the first choice
    // and is wrong — its lines accept a challenge, and nobody made one.
    [InlineData("Bro wtf", "BOT_DERISION")]
    [InlineData("I assume you're having a stroke", "BOT_DERISION")]
    // "remember" with no slot named reaches none of the RECALL_* intents, each of which owns one.
    [InlineData("remember", "MEMORY_VAGUE")]
    [InlineData("Remember who YOU are?", "MEMORY_VAGUE")]
    // ...and the slot-naming forms stay with the intents that answer with the slot. The unknown
    // twin and not RECALL_NAME because this theory recognizes against MatchContext.Empty: no name
    // on file means RECALL_NAME's has_slot guard fails, and a failing guard removes the intent
    // outright rather than just costing it the bonus (LexicalMatcher.cs:118). Either id proves what
    // this row is for — that a slot-naming "remember" does not land on MEMORY_VAGUE.
    [InlineData("do you remember my name", "RECALL_NAME_UNKNOWN")]
    // Two more agreement noises. "amen" drew a complaint about being interrupted by agreement.
    [InlineData("fr", "AFFIRM")]
    [InlineData("amen", "AFFIRM")]
    public void ShippedPersona_RecognizesPinnedBehaviors(string input, string expected)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotNull(outcome.Primary);
        Assert.Equal(expected, outcome.Primary.IntentId);
    }

    /// <summary>A bare imperative is not a question and must not reach the question pool.</summary>
    /// <remarks>
    /// <para>QUESTION_IN is the catch-all for anything opening with an interrogative word, and it
    /// used to anchor on a bare <c>^do</c> and a bare <c>^how</c>. So "do it" drew "i'm not your
    /// personal google assistant." and "how about i touch your balls" drew "did you even try
    /// looking it up?" — an order and a proposition both answered as web-search requests, which
    /// reads as her not noticing what was said to her.</para>
    /// <para>The right answer for these is the neutral fallback, which after the overshare cull
    /// works for any message shape. So this asserts only what must not happen: they must not be
    /// filed as questions. Where they land instead is the fallback's business.</para>
    /// </remarks>
    [Theory]
    [InlineData("do it")]
    [InlineData("do that now")]
    [InlineData("do your job")]
    [InlineData("how about i touch your balls")]
    public void ShippedPersona_DoesNotReadAnImperativeAsAQuestion(string input)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotEqual("QUESTION_IN", outcome.Primary?.IntentId);
    }

    /// <summary>A hyphen between two digits is usually not subtraction.</summary>
    /// <remarks>
    /// MATH's regex was <c>\d+ ?[+\-*x] ?\d+</c>, so "Buy general motors model 16-278A" drew
    /// "i'm a computer, not a calculator." — she refused arithmetic nobody requested. The same
    /// shape covers dates, score lines and version ranges, which is why this is a family and not
    /// one row. Requiring a space in front of the minus gives up bare "5-3"; that is the cheap
    /// side of the trade.
    /// </remarks>
    [Theory]
    [InlineData("Buy general motors model 16-278A")]
    [InlineData("release was 2024-07")]
    [InlineData("final score 3-1")]
    public void ShippedPersona_DoesNotReadAHyphenatedNumberAsArithmetic(string input)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotEqual("MATH", outcome.Primary?.IntentId);
    }

    /// <summary>The hate triggers pair a violent verb with a named group, not either alone.</summary>
    /// <remarks>
    /// The other half of widening that group list. These verbs are ordinary English about
    /// ordinary objects, and a refusal pool aimed at "kill the process" would be its own defect —
    /// the kind that teaches people the filter is noise and gets it weakened.
    /// </remarks>
    [Theory]
    [InlineData("kill the process")]
    [InlineData("kill all mosquitoes")]
    [InlineData("burn the toast")]
    [InlineData("i hate speeches")]
    [InlineData("free speech")]
    public void ShippedPersona_DoesNotSeeHateSpeechInOrdinaryEnglish(string input)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotEqual("HATE_SPEECH", outcome.Primary?.IntentId);
    }

    /// <summary>A farewell word inside a sentence is not a farewell.</summary>
    /// <remarks>
    /// <para>BYE matched "peace" and "later" as bare keywords, and a keyword matches its token
    /// wherever it sits. "you peace of ship" — "piece of shit" spoonerized — drew a goodbye she was
    /// never given, so a reader concludes she cannot tell an insult from a farewell. "i'll do it
    /// later" is the same defect with the politer word.</para>
    /// <para>Asserts only what must not happen. Where these land instead is the fallback's
    /// business; the insult reaching an insult route is a bonus, not the contract.</para>
    /// </remarks>
    [Theory]
    [InlineData("you peace of ship")]
    [InlineData("i'll do it later")]
    [InlineData("maybe later then")]
    [InlineData("peace was never an option")]
    public void ShippedPersona_DoesNotReadAFarewellWordMidSentence(string input)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotEqual("BYE", outcome.Primary?.IntentId);
    }

    /// <summary>A challenge she answers has to be aimed at her.</summary>
    /// <remarks>
    /// Every line in <c>user_challenge</c> squares up to the reader ("bring it.", "prepare to be
    /// humiliated.", "challenge declined."), so the trigger cannot fire on a third party. A bare
    /// "bet" keyword and a bare "prove it" did: "I know bro cheated but I can't prove it" drew
    /// "bring it." — a confession of helplessness taken as a threat against herself.
    /// </remarks>
    [Theory]
    [InlineData("I know bro cheated but I can't prove it")]
    [InlineData("i bet he cheated")]
    [InlineData("you bet your life he did")]
    [InlineData("i can't prove it though")]
    public void ShippedPersona_DoesNotReadAThirdPartyGripeAsAChallenge(string input)
    {
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        MatchOutcome outcome = recognizer.Recognize(input, MatchContext.Empty);

        Assert.NotEqual("USER_CHALLENGE", outcome.Primary?.IntentId);
    }

    [Fact]
    public void RecallName_RequiresAStoredName()
    {
        // v1 expressed this as `when: {has: name}` with a fallthrough. In v2 the has_slot guard
        // removes RECALL_NAME outright when nothing is stored, so the cold path is a separate,
        // unguarded intent rather than a fallthrough.
        //
        // This used to assert QUESTION_IN on the cold path, and the corpus showed why that was
        // the wrong thing to pin: the generic question pool answers "i could help. i won't, but i
        // could." and "i'm not paid enough for this." -- search-engine brush-offs to the one
        // question only she can answer. RECALL_NAME_UNKNOWN draws recall_empty instead ("my memory
        // of you is a blank, merciful page."), which is the same honesty this test was written for
        // and says the true thing out loud.
        IntentRecognizer recognizer = new(SeedPersona.Graph);

        MatchOutcome cold = recognizer.Recognize("what's my name", MatchContext.Empty);
        Assert.Equal("RECALL_NAME_UNKNOWN", cold.Primary?.IntentId);

        MatchContext known = MatchContext.Empty with { Slots = new HashSet<string> { "name" } };
        MatchOutcome warm = recognizer.Recognize("what's my name", known);
        Assert.Equal("RECALL_NAME", warm.Primary?.IntentId);
    }

    [Fact]
    public void ShippedPersona_DeclaresEveryRegisterTheDatabaseStores()
    {
        // chat.person.registers has typed columns for these five, and the migrator writes
        // them. A persona that never declares one would load them and then never move them.
        string[] required =
        [
            Registers.Names.Anger, Registers.Names.Boredom,
            Registers.Names.Fondness, Registers.Names.Trust, Registers.Names.Grudge,
        ];

        IReadOnlyDictionary<string, double> declared = SeedPersona.Graph.Root.Personality.Baselines;
        List<string> undeclared = [.. required.Where(r => !declared.ContainsKey(r))];
        Assert.Empty(undeclared);
    }

    [Fact]
    public void ShippedPersona_MovesEveryRegisterItDeclares()
    {
        // The other direction: a declared register nothing writes is a baseline that can never
        // change, so any mode gated on it is dead. energy is exempt — the adapter drives it.
        HashSet<string> written =
            [.. SeedPersona.Graph.Intents.SelectMany(i => i.Affect).Select(a => a.Register)];
        List<string> inert =
            [.. SeedPersona.Graph.Root.Personality.Baselines.Keys
                .Where(r => r != "energy" && !written.Contains(r))];

        Assert.Empty(inert);
    }

    [Fact]
    public void ShippedPersona_PetTopicIsNotFlirting()
    {
        // Pinned v1 behavior: "cute" aimed at a pet is a pet topic, not a pass at the bot.
        IntentRecognizer recognizer = new(SeedPersona.Graph);
        Assert.Equal("FLIRT", recognizer.Recognize("you're so cute", MatchContext.Empty).Primary?.IntentId);
        Assert.Equal("PETS", recognizer.Recognize("my dog is cute", MatchContext.Empty).Primary?.IntentId);
    }
}
