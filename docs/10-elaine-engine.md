# 10 — Chat Engine (internal name: Elaine)

The personality module. Deterministic, authored, zero generative AI. This doc covers
the new engine design; the old Python `elaine/` package (kept in `_bot_legacy/`) is
the reference implementation and its test suite is the behavior catalog.

## Design contract (carried over from the old engine — its best ideas)

1. **Every word is authored.** Replies come from persona files; the engine selects
   and composes, never generates.
2. **Deterministic.** Same state + same input + same turn = same reply. RNG is seeded
   from the logical turn; time and randomness are injected, never ambient. This buys
   replayable time-travel debugging.
3. **Fail-fast persona validation.** A persona that references a missing state, pool,
   or capture refuses to load — at boot and at hot-reload (a broken edit can never
   take her down; the old file stays live).
4. **Text-only.** The engine returns words (and optionally an emoji reaction);
   it never moderates, never acts on the server.

## What's new vs the old FSM

| Old | New |
|---|---|
| One state owns everything (CHAT had ~100 transitions) | **Activity stack** (idle / rps / argument / story-scene…) + orthogonal **mood & relationship dimensions** — persisted, so nested activities survive restarts |
| First-match-wins transition order (endless shadowing bugs) | **Scored selection**: all matchers run, specificity + topic-affinity + guard bonuses; declared order only breaks ties. Validator ships a shadowing report. |
| One intent per message | **Multi-intent**: primary response + side-effect acknowledgments ("hi, I'm Sam and why do you hate me" → both halves) |
| Keyword/regex/fuzzy only | + **semantic tier**: ONNX embeddings rescue off-script phrasing; never overrides a confident lexical match, only rescues misses |
| `once:`/`cooldown:` reset every message (broken in prod) | fired-log persisted in chat.person |
| Single topic slot | **Topic stack** (cap 5, recency decay) + pending-question queue (she notices unanswered questions) |
| One line from one pool | **Composition**: optional mood fragment + core + optional callback tail (episodic recall via pgvector) |
| Logical-turn time only | Logical clock stays pure; the **adapter** feeds wall-clock signals (long-absence greeting tiers, multi-day grudge decay, 3–6 am pool, mood-of-the-day seed, seasonal overlays) |

## Pipeline (per mention/reply)

```
gate (mention/reply only, budget check, kill switch)
  → load: chat.person (Redis hot-cache → Postgres), session keys
  → normalize (case-preserving spans, style detection: CAPS / wall-of-text / emoji-flood)
  → understand: lexical matchers scored → below threshold? embed → semantic match
  → decide: activity layer picks primary + side-effects; affect engine updates
     registers (incl. apology-sincerity check, thanked-reaction, repeat-ping check)
  → compose: fragment + core + callback (pgvector top-K episode, relevance-gated)
  → persist: person/facts/episodes/relationship_event (one transaction), session keys
  → deliver: typing delay ∝ length → reply (or emoji reaction only)
```

Budget: worst case ~50 ms compute (embedding path); typical path a few ms.

## Persona format (v2, YAML)

Directory, not a single file — hot-reload watches the whole set:

```
persona/
├── sonarr.yaml          # root: activities, personality baselines, tiers, modes
├── intents/*.yaml       # intent bundles: lexical patterns + semantic examples
├── pools/*.yaml         # response pools (migrated from _sonarr_pools.yaml)
├── stances.yaml         # opinion registry (topic → stance → pool)
└── overlays/*.yaml      # seasonal/daypart overlays (october, december, latenight)
```

v1 → v2 is a scripted migration (Sonarr.Migrator) + hand-polish; pools carry over
nearly verbatim. Validator checks: reachability, dangling refs, capture safety,
shadowing report, pool coverage per mode, overlay collisions.

## Feature hooks (from the picked list, where they live)

- Relationship tiers & privileges → tier field on chat.person; tier-gated guards in
  intents; tier-up gets an authored moment.
- Assigned nicknames → tier-triggered, pool-drawn, stable per user (stored).
- Favorites/least-favorites, taking sides, rivalry commentary → queries over
  relationship_event + trust ordering, surfaced through authored lines.
- Fact confidence & hedging → confidence on chat.fact (reinforce on repeat).
- `/memories`, `/relationship`, forget-me → read/delete through the same services.
- Edit call-outs, repeat-ping call-outs, thanked-reaction → adapter events (08).
- Server-event memory ("last time this many people were online…") →
  chat.guild_state.event_log fed by PresenceSampler.
- Mood of the day / seasonal overlays → DailyTick reseed + overlay activation.
- `/opinion` → embedding of recent channel topic (metadata-safe: embeds only the
  topic centroid of the last few messages, discarded after) → stance registry.

## Testing

- **Behavior catalog**: every pinned behavior from `_bot_legacy/tests/
  test_logical_response.py` rewritten against the new engine (same scenarios, new
  format). This is the regression floor.
- **Golden conversations**: scripted multi-turn dialogues with full state assertions,
  run deterministically (injected clock/RNG).
- **Persona lint in CI**: the validator runs on every persona change; shadowing
  report failures block merge.
- **Trace replay**: opt-in interaction traces (07/panel) can be replayed step-by-step
  locally with exact state — the payoff of the determinism contract.
