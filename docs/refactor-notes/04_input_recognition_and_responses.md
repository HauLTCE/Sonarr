# Task 3 + 6 — Better input recognition & taming the 100+ response files

I pulled the OLD `sonarr.service` journal off the server (read-only,
`journalctl -u sonarr.service`, 7546 lines, still present at 192.168.1.101).
Raw dump saved locally to `_server_log_raw.txt` and **gitignored** (it contains
user messages + IDs — never commit it).

## What the real logs show (one boot window)
- **278** user inputs classified.
- Decision outcomes: agree **67**, verifier-consensus **21**, early-exit **22**,
  embedding-only **12**, **cold-fallback 0**.
- Final scores (n=65): min 0.185, **median 0.592**, max 0.775.
  `<0.3: 2 | 0.3–0.55: 14 | ≥0.55: 49`.

### Reading
- The classifier is **confident when it fires** — most finals clear the 0.55
  early-exit bar, and it almost never bottoms out to a cold default.
- The gap (278 inputs vs ~122 logged decisions) means **lots of messages
  short-circuit before a full decision** — they don't match a category strongly,
  so the bot likely says something generic. The lever is **coverage of intents**,
  NOT threshold tuning. Lowering thresholds would just add false matches.

## Task 3 — recognize input better WITHOUT an LLM (cheap wins, ranked)
1. **Tune from real misses.** Grep `_server_log_raw.txt` for the inputs that got
   low finals or no decision, cluster them, and add label phrases for the
   intents you actually get. This is the single highest-value change and it's
   data-driven, not guesswork.
2. **Synonym/keyword pre-pass.** Many misses are short ("yo", "wyd", "u up").
   A tiny normalized-keyword map → category, checked BEFORE the embedding tier,
   catches the high-frequency slang for ~free.
3. **Tighten `_discover_labels` descriptions.** Embedding retrieval matches on
   the LABEL text. Richer, more natural label descriptions improve Tier-1 recall
   with zero model changes.

## Task 6 — collapse the 100+ response files into "aspect" buckets
**Current:** `sonarr/responses/main/` has 47+ files, one per hyper-specific
intent (bot_age, bot_gender, bot_creator...). Each is a `LABEL=` + a list.

**Proposed (keeps it smart, cuts the file sprawl):**
- Group the ~120 hyper-specific labels into ~12–15 **aspect buckets**
  (identity, mood, relationship, insult-response, meta/bot, smalltalk,
  knowledge-deflect, etc.). One file per bucket, each holding sub-intents as
  dict keys instead of separate files.
- Keep the two-tier classifier; just point it at bucket labels + a within-bucket
  refine. Fewer, broader labels = better Tier-1 recall (the coverage fix from
  task 3) AND far less file sprawl.
- A "general/any-aspect" fallback pool of witty-but-noncommittal lines for the
  short-circuit inputs, so a low-confidence match still feels in-character
  instead of generic.

**Risk:** this is a content + classifier-label refactor. Medium risk (changes
what the bot says). I'd do it on a branch, bucket-by-bucket, with the old files
kept until the new mapping is validated against `_server_log_raw.txt` replays.

## My ask
Want me to start with task-3 #1+#2 (data-driven label additions + keyword
pre-pass)? Those are low-risk and measurable. The big bucket refactor (task 6)
I'd schedule with you watching, same as the cursor work.
