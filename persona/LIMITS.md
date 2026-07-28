# Accepted limits

What the persona suite deliberately does not check, and why. Every entry here was a real
decision — a guard that could have been written and was not, or one written narrower than it
could have been — and it is here so nobody re-litigates it from scratch or, worse, "fixes" it
by widening a guard until it starts failing on lines that must ship.

The rule behind most of these: **a guard that fires on legitimate lines gets weakened until it
catches nothing.** A pattern that cannot decide between two shapes does not belong in a guard.
The undecidable cases were fixed by hand instead, and the hand-fixing is what these notes
record.

## Moderation claims — `ShippedPersona_ClaimsNoModerationItCannotPerform`

- **Future tense is not checked.** "i'll ban you", "timeout incoming if you mess it up again."
  A threat is in voice; only a *completed* action is a lie the reader can check by looking at
  the member list.
- **Bluster about having the power is not checked.** "i have a timeout button", "siri doesn't
  have a timeout button. i do." Hot air about capability is in voice. Announcing an effect is
  not.
- **A bare duration as its own sentence is not checked.** "i'm smarter than you. 2 hours." is a
  verdict she cannot hand down; "go study. 30 minutes. you'll thank me." is ordinary rudeness
  about how the reader spends their own time. Same string shape — the difference lives in the
  *preceding* sentence. A pattern catching both would fail on lines that must stay, so those
  verdicts were found and fixed by hand and the pattern is deliberately absent.
- **Figures of speech are not checked.** "i'm muting my emotional sensors", "putting you in the
  corner." The guard requires the object to be the reader or their message.
- **`memory_forget` is exempt from the delete patterns.** `/memories forget` really does drop
  the fact, so "deleted." there is true. A claim is only a lie when nothing backs it.

## Dangling clauses — `ShippedPersona_HasNoDanglingSubordinateClause`

- **`because` is not checked at all.** "because i'm literally better than you in every metric."
  is a complete answer to a why-question, and whether one was asked lives in the pool, not the
  string. The `joke_your_mom` lines of the same shape were fixed by hand.
- **A comma-joined main clause passes.** "if you're bored, go outside." is a whole sentence. The
  missing comma turned out to be the real tell, and it caught a genuine defect that way.
- **Elliptical idioms pass.** "since day one.", "if you say so."

## Overshare presupposition — `FallbackPool_DoesNotPresupposeAnOvershare`

- **Pinned to `neutral_statement` only, not to the phrasing.** "keep that to yourself." is
  correct in `user_oversharing` (the route guarantees somebody overshared) and in
  `user_affection` / `user_love` (a declaration genuinely was volunteered). The defect is the
  pairing of line and pool, so the guard names the pool.

## What no automated check can decide

- **Whether a reply is *right*.** `CorpusReplyTests` asserts only that she answers, that the
  answer renders, and that the same input twice gives the same words. "Perfect and in-context"
  needs judgement, which is what the review sheet and the sub-agent pass are for — and a
  sub-agent is a second opinion, not an oracle. Same sheet, different run, different verdicts on
  the borderline rows, so only clear-cut rows are pinned as golden.
- **General quality.** The corpus is 438 distinct messages from a handful of people. That is
  enough to find gaps and nowhere near enough to claim the bot is good in general.
- **Coverage is thin and the number is deliberately not a target.** As of 2026-07-29 the corpus
  reaches **58 of 129 intents and falls through on 243 rows (55.5%)**. Fallthrough means the reply
  came from a generic pool, so it was not *about* the message. That is the honest headline of the
  whole exercise and it is printed on every run by
  `CorpusReplyTests.CoverageAndFallthroughAreReported`, which asserts nothing about it — driving
  the number down by authoring intents to match this one corpus would fit 438 messages from a few
  people and generalise to nothing. The 71 unreached intents are mostly unreached because nobody
  happened to say those things, not because they are broken.
- **Nothing asserts against the legacy reply.** `CorpusRow.LegacyOut` came out of a different
  engine; matching it would pin the old bot's behaviour instead of testing the new one's. It is
  evidence of what was *asked*, nothing more.
- **The corpus is private and gitignored.** `CorpusFactAttribute` skips rather than fails when
  it is absent, so a clone without `training-data/` reports green on a suite that did not run.
  The skip reason names the script that regenerates it.

## Structural

- **A pool with authored lines and no intent pointing at it is invisible.** Two were found this
  way (`joke_dad`, `question_hypothetical` with 19 unreachable lines). There is no guard for it;
  it takes a deliberate sweep.
- **Pool lines never join.** `LinePicker.Pick` draws exactly one line and `ReplyComposer` never
  concatenates two, so every line must stand alone as a whole reply. A mood fragment can
  prepend, but "mm." is not a main clause.
