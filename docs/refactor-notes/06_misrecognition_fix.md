# Why the bot misrecognized — and what I changed (2026-05-31)

You reported the bot mis-reading normal chat (and sometimes timing people out).
I traced it through the real server logs. Here's the plain-English version.

## The three root causes (all fixed)

### 1. The confidence score was thrown away (the big one)
The classifier computes a real "how sure am I" score (0.0–1.0) per message.
But `async_classify` returned a hardcoded `3` on EVERY successful path. The
pipeline gate (`confidence < 2`) therefore almost never fired, so the bot was
**forced to pick one of ~300 categories for every message**, no matter how bad
the fit. A 0.18 garbage match was treated like a 0.85 great match.

When a message fit nothing well, the nearest category was often a HOSTILE one
(`user_insult`, `user_confusion`), so normal chat got snapped at.

**Fix:** return the real score; route anything below `CONFIDENCE_FLOOR = 0.42`
to the broad, on-brand `general_aspect` pool instead of a confident wrong guess.

### 2. Broad keyword overrides trapped innocent words
Before the ML even ran, regex shortcuts force-classified messages at full
confidence. The worst: `blame|flame|roast|drag|call out|expose|cook` →
`user_insult`. So "I'm **cook**ing", "don't **drag** this out" → bot insults you.
**Fix:** removed that trap; "roast/insult me" now → `roast_requester` (playful).
Also `smash` → inappropriate now needs a person target (not "smash bros").

### 3. Timeouts hid in normal-conversation pools
~40 normal pools (jokes, questions, requests) had stray `TIMEOUT:` lines, so
even a CORRECT classification could randomly mute a chatter.
**Fix:** timeouts now only fire for genuine-abuse categories
(`TIMEOUT_ALLOWED_CATEGORIES` in pipeline.py); elsewhere the sass sends but the
punishment is stripped.

## Real misfires from your logs that these fix
- "I need a medic", "*hugs you*", "explain your existence" → were `user_confusion`
- "roast me" / "can you roast me" → were `user_insult` (now playful)
- "bye", "testing 123" → were `disruptive_behavior` (a category that doesn't
  even exist as a file → dead-end "What?")

## The ONE knob to tune after watching live traffic
`CONFIDENCE_FLOOR` in `sonarr/input_check/pipeline.py` (currently **0.42**):
- Bot feels **too dismissive / general** (falls back too often) → LOWER it (e.g. 0.35).
- Bot still **mis-snaps** at normal chat → RAISE it (e.g. 0.50).
The `[Classify] LOW CONFIDENCE ...` log lines show exactly what's falling back —
watch those for a day, then nudge the floor. No model retraining needed.

## What I did NOT do (needs ML stack live to validate safely)
Reducing the ~300 categories to ~15 buckets — documented in `05_response_bucket
_collapse_plan.md`. Fewer, broader categories would further reduce neighbor-
confusion, but it changes routing and must be validated against live traffic.
The confidence floor already mitigates the crowding for now.
