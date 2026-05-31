# 02 — The AI Brain (sonarr/)

This is the strangest and most over-engineered part of the whole project, and
also the part with the most personality. There is **no LLM**. The "AI" is a
classifier that maps your message to one of ~120 categories, then picks a
hand-written canned response from that category's pool.

## The two-tier classifier

`sonarr/input_check/classifier.py` runs a genuine two-stage NLP pipeline:

- **Tier 1 — Retrieval.** A SentenceTransformer embeds your message and does
  cosine similarity against ~120 pre-computed label embeddings. Sub-millisecond,
  returns the top N candidates. There's also BM25 and a cross-encoder wired in.
- **Tier 2 — Verification.** The top candidates get run through **three**
  separate MNLI models *concurrently*:
  - `facebook/bart-large-mnli`
  - `MoritzLaurer/DeBERTa-v3-base-mnli-fever-anli`
  - `roberta-large-mnli`

  Their votes get merged into a final `(category, score)`.

All inference runs in a dedicated `ThreadPoolExecutor` so the Discord event
loop never blocks. The heavy models load in a background thread at startup, so
the bot comes online before the models are ready (early messages just fall
through to embedding-only or a default).

That is three transformer models and an embedding model, in memory, on a
hobby Discord bot, to decide whether you called it "bro." It is glorious
overkill, and honestly kind of impressive that it's structured this cleanly.

## Why this is both clever and cursed

**Clever:** the tier thresholds (`EMBED_EARLY_EXIT = 0.55`,
`TIER2_OVERRIDE_SCORE = 0.7`, etc.) let it skip the expensive tier when the
cheap one is confident. Labels are auto-discovered from a `LABEL = "..."`
string at the top of each response file, so adding a category is just dropping
a new file. That's a nice convention.

**Cursed:** it needs PyTorch, transformers, sentence-transformers, spaCy,
NLTK (with VADER), and rank_bm25 — all to run a glorified `if "bro" in msg`.
The models are mounted read-only in the Docker image (HF_HOME / NLTK_DATA with
offline flags) because re-downloading them on every container start would be
absurd. There are even `scripts/fix_nltk.py` and `scripts/fix_spacy.py`,
which tells you those data dependencies have bitten before.

## The effects mini-language

Responses aren't just text. A response string can carry a **prefix DSL**
parsed by `sonarr/responses/effects.py`:

```
TIMEOUT:5m:Don't wake me up.      → times the user out for 5 minutes
REACT:🤡:Honk honk                → adds a reaction + replies
DOUBLE:first||second              → sends two messages with a delay
RENAME:Clown:You're dressed for it → renames the user
DELETE:message                    → deletes the user's message
WHISPER:msg                       → wraps reply in ||spoilers||
SLOW:msg                          → edits word-by-word for dramatic typing
STICKER:🎭🤡💀:msg                → multiple reactions at once
SEARCH:GOOGLE:msg                 → appends a search link
```

So a single canned string like `TIMEOUT:5m:Don't wake me.` is both the
message *and* a moderation action. This is genuinely cute and a clean little
parser (`parse_response` → `ResponseEffect` dataclass → `apply_effects`).

The wart: `selector.py` has a *second*, independent `_strip_effects` that
re-parses the same prefixes by hand with `response.split(":")`. Two parsers
for one DSL means they can drift. They already disagree on edge cases (the
selector version doesn't know about `SLOW`, `STICKER`, or `SEARCH`).

## Sleep mode

`get_sleep_state()` ([cog.py:29](../cogs/sonarr_ai/cog.py#L29)) makes the bot
"sleep" at night (server time UTC+7). 22:00–06:00 it hard-refuses with
`SLEEP_RESPONSES` ("im literally unconscious rn leave me alone"). There are
even **grace ramps**: 21:30–22:00 the chance of a sleepy response rises
linearly from 0→100%, and 06:00–06:30 it falls 100→0%. It's gated behind
`SLEEP_MODE_ENABLED` env var (off by default). Completely unnecessary, weirdly
charming, and the cleanest probability-ramp code in the repo.

## The personality

`sonarr/responses/main/gender_correction.py` is the thesis statement: the bot
is a girl, insists on "queen"/"ma'am", and has dedicated comeback pools for
being called bro/dude/man/guy/sir. There's a whole `misgendering_memory` table
(per user, per guild) so it can *remember* you did it. Petty, persistent, and
fully committed to the bit.
