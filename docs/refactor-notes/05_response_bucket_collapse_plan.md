# Task 6 (part 2) — The destructive 100+ file collapse (DO WITH BOT LIVE)

I did the **safe half** already (committed): added `general_aspect` broad pool +
fixed the dead `disruptive_behavior` fallback + keyword pre-pass. That alone
fixes the coverage gap the logs showed.

The **file-count reduction** (merging ~200 response modules into ~15 buckets) I
did NOT do, for one honest reason: I can't run the ML stack here (no torch/nltk),
so I cannot verify the classifier still routes correctly after merging labels.
Merging blind could quietly degrade responses in ways invisible to me. This is
the procedure to do it WITH the bot running so you can watch routing.

## Why it's safe to defer
Nothing is broken now. 200 small files is a tidiness problem, not a bug. The
classifier already works (median final 0.592, zero cold-fallbacks).

## The bucket map (proposed ~15 buckets)
Group the `main/*.py` modules by prefix — the prefixes already ARE the buckets:
- `bot_*` (17 files) → **identity** bucket
- `question_*` (~40) → **questions** bucket
- `topic_*` (~50) → **topics** bucket
- `social_*` (~25) → **social** bucket
- `user_*` (~60) → **user_state** bucket
- `disruptive_*` (~30) → **disruptive** bucket
- `request_*` (~30) → **requests** bucket
- `slang_*`, `joke_*`, `math_*`, `roast_*`, `mixed_*`, `gossip_*`, `neutral_*`
  → one bucket each

## The safe procedure (per bucket, one at a time)
1. Create `responses/buckets/<bucket>.py` with a dict:
   `INTENTS = {"bot_age": (LABEL, RESPONSES), "bot_gender": (...), ...}`.
2. Keep the classifier returning the FINE key (bot_age), but load labels/pools
   from the bucket dict instead of 17 files. The classifier logic doesn't change
   — only WHERE the data lives.
3. **Validate before deleting:** with the bot live, replay the inputs in
   `_server_log_raw.txt` (or just use the bot for 10 min) and confirm the same
   categories come back. The `[Classify] FINAL` log line shows routing.
4. Only after a bucket validates, delete its old per-intent files.
5. One bucket = one commit = one revert point.

## Why keep the fine keys
The keyword pre-pass and the response selector both key on fine names
(bot_gender, etc.). Collapsing to coarse buckets (just "identity") would lose
resolution and make the bot dumber — the OPPOSITE of the goal. Buckets are a
STORAGE change (fewer files), not a CLASSIFICATION change (same granularity).

## Estimated effort
~15 buckets x (write + validate + delete) ≈ a focused afternoon WITH the bot up.
Net: ~200 files → ~15, zero loss of response granularity.
