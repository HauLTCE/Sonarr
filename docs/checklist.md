# Sonarr Rewrite — Build Checklist

Derived from [docs/](docs/) (01–11) and [`what user need.md`](what%20user%20need.md).
Status legend: `[ ]` not touched · `[~] `in progress · `[/]` done · `[X]` blocked/problem, leave for later

Ordering follows the phase plan in [01-overview.md](docs/01-overview.md#scope-source-of-truth).
Rules that gate every item: users only see "Sonarr" · no generative AI · must run on the
J2900 · nothing durable lost on restart.

---

## Phase 0 — Repo hygiene & prep

- [/] Move the Python bot to `_bot_legacy/` (reference implementation + behavior catalog source)
- [/] Keep `_bot_legacy/tests/test_logical_response.py` reachable — it is the conformance spec
- [/] `.gitignore` for .NET (`bin/`, `obj/`, `*.user`), Node (`node_modules/`, `.next/`), `.env`
- [/] `.editorconfig` — C# style, analyzers as warnings-as-errors on `Sonarr.Elaine`
- [/] `README.md` at root: what this repo is, how to run dev stack, pointer to `docs/`
- [/] Decide dev secret handling (`.env` + `dotnet user-secrets` for local)

## Phase 1 — Foundation

### Solution scaffold ([02-architecture.md](docs/02-architecture.md#solution-structure))

- [/] `Sonarr.slnx` (SDK 10 default format)
- [/] `src/Sonarr.Domain` — entities, domain models, `IService`/`IRepository` contracts, no deps
- [/] `src/Sonarr.Elaine` — engine, pure logic, no Discord/DB/HTTP references
- [/] `src/Sonarr.Application` — service impls, folder per module (Music/Levels/Moderation/Chat/Config/Reminders/Social/Auth)
- [/] `src/Sonarr.Infrastructure` — repositories, `SonarrDbContext`, Redis, ONNX, Lavalink4NET wiring
- [/] `src/Sonarr.Bot` — Generic Host: gateway, interaction modules, background services, API controllers
- [/] `src/Sonarr.Migrator` — one-shot console app
- [/] `tests/Sonarr.Elaine.Tests`, `tests/Sonarr.Application.Tests` (xUnit)
- [/] Enforce dependency direction `Bot → Application → Domain ← Infrastructure` (arch test or review rule)

### Infrastructure ([03-stack.md](docs/03-stack.md), [11-deployment.md](docs/11-deployment.md#compose-stack-new))

- [/] Dev `docker-compose.yml`: postgres:17 + pgvector, redis:7 (`maxmemory 256mb allkeys-lru`)
- [/] `Sonarr.Bot` Dockerfile (multi-stage, `network_mode: host` in prod compose)
- [/] Prod compose skeleton at `deploy/` — bot, web, postgres, redis; music stack deliberately absent
- [/] `.env.example` — DISCORD_TOKEN, LAVALINK_URI/PASSWORD, PG/REDIS conn strings, ADMIN_USER_IDS, PANEL_BASE_URL

### Config system ([04-database.md](docs/04-database.md#coreguild_config), [05-caching.md](docs/05-caching.md#config--flags-area-cfg))

- [/] Boot-time env/config validation — fail loudly on bad config
- [/] `IGuildConfigService` + repository over `core.guild_config` (jsonb, typed + validated per key)
- [/] Redis `cfg:guild:{guild}` cache with write-invalidation ("changes apply immediately")
- [/] `/config` command surface with grouped subcommands + autocomplete
- [/] Config export / import (JSON) through the same service

### Slash-command core ([07-commands.md](docs/07-commands.md#design-rules))

- [/] Discord.Net 3.20.x gateway host + interaction framework wiring
- [/] Command registration strategy: per-guild in dev, global in prod
- [/] Controller-layer input validation convention
- [/] Error pipeline: catch → Serilog structured log → friendly one-liner + case id, never a stack trace
- [/] Ephemeral-by-default rule for personal replies (`/privacy`, `/memories`, all errors)
- [/] Global command flood guard (`rl:cmd:{user}`, 10 s sliding)

### Kill switches & health

- [/] `core.feature_flag` table + `IFeatureGate` (config + panel toggle), checked at Controller layer
- [/] Disabled module answers "this feature is currently off" rather than vanishing
- [/] `/feature module on|off` (admin)
- [/] `SelfTest` service: Postgres, Redis, Lavalink, Discord perms — on boot + hourly, one green/red line on change
- [/] `/checkperms` — bot audits its own permissions per feature
- [/] `/status` and `/ping`
- [/] Serilog: structured console sink (the CLI/`docker logs` view) + rolling file
- [/] Lightweight metrics counters exposed at `GET /api/metrics`

### Database groundwork ([04-database.md](docs/04-database.md))

- [/] `SonarrDbContext` (EF Core 10) + pgvector enabled (`pgvector-dotnet`)
- [/] Schema-per-area convention: `core`, `levels`, `mod`, `music`, `chat`, `social`, `web`, `stats`
- [/] Conventions: Discord ids `bigint`, all timestamps `timestamptz`, `created_at`/`updated_at`
- [/] Migration 1 — `core`: guild, guild_config, member, feature_flag, job
- [/] `core.job` poller contract: `run_at` indexed, claim with `FOR UPDATE SKIP LOCKED`
- [/] Redis behind repository-shaped interfaces (`ISessionCache`, `ICooldownStore`, …) — never `IDatabase` in services
- [/] Key-prefix convention `sonarr:{area}:{...}`; a key without a TTL is a build error in review

---

## Phase 2 — Parity+ (old bot retires at the end of this phase)

### Music ([07-commands.md](docs/07-commands.md#music), [03-stack.md](docs/03-stack.md#runtime--bot))

- [/] Lavalink4NET 4.2.x (`.Discord.Net`) wiring against the **existing untouched** Lavalink 4.2.2
- [/] Migration — `music`: playlist, playlist_track, play_history, track_rating, user_prefs — landed in Migration 1
- [/] `IMusicService` (domain models only, no Discord types) + repositories
- [/] `/play` — search + URL; playlist links ask confirmation ("adds 47 tracks — sure?")
- [/] `/pause`, `/resume-playback`, `/stop` (DJ), `/seek`, `/replay`
- [/] `/skip` — vote-skip at 8+ listeners (`music:voteskip:{guild}`), DJ bypass
- [/] `/undo-skip` — 10 s window (`music:undo_skip:{guild}`)
- [/] `/queue [page]`, `/remove position` (own vs DJ), `/duplicate-cleanup` (DJ)
- [/] `/shuffle`, `/playnext`, `/loop track|queue|off`
- [/] `/fairqueue on|off` — round-robin across requesters (DJ)
- [/] `/previous` from history
- [/] `/nowplaying` — embed + buttons, requester, tracks-until-yours; single edited message (`music:np_msg:{guild}`)
- [/] 👍/👎 rating buttons on nowplaying → `music.track_rating`
- [/] `/volume 0–150` (DJ) with per-user preference remembered in `music.user_prefs`
- [/] `/filter bassboost|nightcore|karaoke|speed|clear` (DJ)
- [/] `/grab` (+ context menu) — DM the current track
- [/] `/playlist save|load|list|delete` — per-guild named playlists, owner-scoped
- [/] `MusicSessionSnapshotter` (30 s) → `music:session:{guild}`, 24 h TTL
- [/] `/resume` — restore crashed session queue + position
- [/] `/musicstats`, `/mytracks`, `/toptracks`
- [/] `/autoplay on|off|smart` — smart seeded from requester history, avoids net-negative tracks
- [/] **VC-drag fix**: `VoiceStateWatcher` reconnects the player when the bot is dragged; must not crash
- [/] Regression test for the VC-drag case (explicit requirement)
- [/] Auto-pause on empty channel + 300 s delayed disconnect

### Levels ([07-commands.md](docs/07-commands.md#levels))

- [/] Migration — `levels`: progress, reward, season, season_result — landed in Migration 1
- [/] `XpOnMessage` handler: XP with 60 s cooldown (`rl:xp:{guild}:{user}`), streak day-touch, first-message bonus
- [/] `VoiceXpAccrual` (60 s): only when ≥2 humans present and user unmuted (anti-AFK)
- [/] `presence:voice:{guild}:{user}` lifecycle in `VoiceStateWatcher`
- [/] `ActivityFlusher` (60 s): batch message-count/last-active Redis → `core.member`
- [/] `/level`, `/rank` (progress-bar card, streak shown), `/leaderboard [season]`, `/compare`, `/userstats`
- [/] Role rewards on level-up + levelup channel
- [/] `/config levels …` — rewards add/remove, levelup channel, XP event multiplier, decay toggle, channel weights

### Moderation ([07-commands.md](docs/07-commands.md#moderation))

- [/] Migration — `mod`: case (bigserial case_id), `mod.infraction_summary` view — landed in Migration 1
- [/] Case-numbered logging: DB row + Serilog structured console line for every action
- [/] `/warn` with reason autocomplete from templates
- [/] `/kick`, `/ban`, `/unban`
- [/] `/tempban user duration [reason]` — auto-lift via `core.job`, survives restarts
- [/] `/timeout`, `/untimeout`
- [/] `/purge count [from] [contains] [bots] [preview]` — preview is a dry run, deletes nothing
- [/] `/slowmode duration|off` — logged as a case
- [/] `/case id`, `/modlog [user] [page]`, `/userinfo user` (+ context menu)
- [/] `AntiSpam` v2: identical-flood, mass-mention, invite-link → configured action + case (`rl:spam:{guild}:{user}`)
- [/] `/config moderation …` policy surface

### Server management & utility

- [/] `WelcomeFlow` — welcome/farewell embeds + autorole on join/leave
- [/] `JobScheduler` (15 s poll) — reminders, recurring reminders, tempban lifts, announces, capsules, season closes
- [/] `/remind when what` (timezone-aware), `/reminders list|cancel` incl. recurring
- [/] `/timezone zone` (autocomplete), `/timestamp time [format]`
- [/] `/announce channel message [schedule]` — immediate or scheduled/recurring
- [/] `/roleinfo`, `/serverinfo`, `/avatar` (+ context menu)
- [/] `/choose`, `/roll`, `/color`, `/enlarge`
- [/] `/privacy` — ephemeral summary + panel link, must agree with [06](docs/06-data-and-privacy.md)
- [/] `CommandUsageCounter` → `stats.command_usage`
- [/] `PresenceSampler` (5 min) → `stats.activity_sample`
- [/] Autocomplete everywhere an argument has known values

### Data migration ([04-database.md](docs/04-database.md#data-migration-map-sonarrmigrator-one-shot))

- [/] Source of truth is **`/root/sonarr/`**, not the stale `/root/sonarr-data/`
- [/] `bot_data.db` levels → `levels.progress` (voice/streak start 0)
- [/] `bot_data.db` guild_config → `core.guild_config` (drop games/dungeon/economy keys)
- [/] `data/elaine.db` user_state → `chat.person` (split blob: registers/slots/state; fired_log & stack fresh)
- [/] `data/elaine.db` episodic → `chat.episode`
- [/] `data/elaine.db` global_state → `chat.guild_state`
- [/] `playlists.json` → `music.playlist` + `playlist_track`
- [/] Economy/games/adventure/dead AI-cache tables: **not migrated**
- [/] Idempotent, re-runnable, prints a row-count reconciliation table

### Cutover ([11-deployment.md](docs/11-deployment.md#migration--cutover-end-of-phase-2))

- [ ] Freeze old bot (`systemctl stop sonarr`), WAL-checkpoint SQLite, copy data
- [ ] Run migrator → Postgres; verify reconciliation
- [ ] Register slash commands; run SelfTest + `/checkperms`
- [ ] Smoke-test music (incl. VC-drag) on the test guild, then the real one
- [ ] `systemctl disable sonarr`; new stack `restart: unless-stopped`
- [ ] 2-week rollback window: old venv + DB untouched
- [ ] After the window: archive `/root/sonarr`, delete `sonarr-docker*`, `sonarr-data`, backups-of-backups
- [ ] Switch server SSH to key-only auth (currently root/password `12345`) — do this on the first deploy

---

## Phase 3 — Chat core ([10-elaine-engine.md](docs/10-elaine-engine.md))

### Engine skeleton (`Sonarr.Elaine`, pure)

- [/] Determinism boundary: injected `IClock` + seeded RNG (seed = logical turn); nothing ambient
- [/] Migration — `chat`: person, fact, episode, relationship_event, stance, stance_agreement, guild_state, intent_embedding
- [/] Persona v2 loader: directory-based (`sonarr.yaml`, `intents/`, `pools/`, `stances.yaml`, `overlays/`)
- [/] Persona validator: reachability, dangling refs, capture safety, pool coverage per mode, overlay collisions
- [/] Shadowing report from the validator
- [/] Fail-fast rule: invalid persona refuses to load at boot **and** at hot-reload (old file stays live)
- [/] Normalizer: case-preserving spans + style detection (CAPS, wall-of-text, emoji-flood)
- [/] Lexical matchers (keyword / regex / fuzzy) with **scored selection** — specificity + topic-affinity + guard bonuses; declared order breaks ties only
- [/] Multi-intent: primary response + side-effect acknowledgments
- [/] Activity stack (idle / rps / argument / story-scene…) — persisted in `chat.person.activity_stack`
- [/] Orthogonal mood & relationship dimensions (registers: anger, boredom, fondness, trust, grudge)
- [/] Topic stack (cap 5, recency decay)
- [/] Pending-question queue + unanswered detection
- [/] Fired-log persisted (`once:`/`cooldown:` no longer reset every message)
- [/] Composition: optional mood fragment + core line + optional callback tail
- [/] Engine returns text (+ optional emoji reaction) only — never moderates, never acts

### Persona migration

- [/] ~~Scripted~~ v1 → v2 conversion (done directly, not as a Migrator verb — a one-shot converter has no second use)
- [/] Hand-polish pass + validator clean
- [/] Persona hot-reload watcher in `Sonarr.Bot`: validate, atomic swap only if valid, log either way
- [/] Persona directory mounted as a volume (`./persona:/app/persona`)

### Pipeline & adapter (`ChatPipeline`, [08](docs/08-background-services.md#event-driven-gateway-handlers-not-timers))

- [/] Gate: mention/reply only + engagement budget (`chat:budget:{channel}`) + kill switch
- [/] Load: `chat:hot:{guild}:{user}` → Postgres fallback; write-through on save (row wins a stale snapshot)
- [/] Session state `chat:session:{guild}:{user}` (10 min sliding) — "message 6 of a back-and-forth" vs "first contact in days"
- [/] Channel ring buffer `chat:ring:{channel}` (metadata only, cap 10, 15 min) — pushed for ambient messages too; rapid-fire + two-people-talking *reads* still unused
- [/] Repeat-ping detection `chat:lastping:{guild}:{user}`
- [/] Affect engine: apology-sincerity check, thanked-reaction, register updates (`ChatAffect`)
- [/] Persist person/facts/episodes/relationship_event in **one transaction** + session keys (commit before she speaks; Redis writes after)
- [/] Deliver: typing delay ∝ length, then reply (typing indicator held for the pause)
- [/] `EditWatcher` — `chat:replied:{channel}:{msg}` hit → authored "stealth edit" call-out (`user_receipts`, no turn, no register movement)
- [/] Wall-clock signals from the adapter: absence tiers, multi-day grudge decay, 3–6 am pool, mood-of-the-day seed, seasonal overlays (`ClockSignals`; `AbsenceTier` derived but not yet read by a pool)
- [~] Worst-case budget check: ~50 ms compute on the embedding path — lexical half pinned (`TurnBudgetTests`: a full miss runs every pattern of every intent, ceiling 5 ms/turn). The embedding half is Phase 4's J2900 benchmark below.

### Commands & tests

- [/] `/relationship [user]` — authored descriptions, no numbers (`relationship_<tier>` pools; tier recomputed from trust, not read from the stored column; another user's tier is deflected, not reported)
- [/] `/memories`, `/memories forget fact` (autocomplete from your facts) — `/memories list` + `/memories forget`, both ephemeral, all four openers authored
- [/] Behavior catalog: every pinned behavior from the legacy test suite, rewritten — the regression floor (`BehaviorCatalogTests`; legacy behaviors with no v2 route — bare-number reactions, negation routing, hello counting, assigned nicknames — are recorded there as comments with the reason, not dropped)
- [/] Golden conversations: scripted multi-turn dialogues with full state assertions (`GoldenConversationTests`; nothing to inject — no clock, no ambient RNG, so a fixed salt is the whole seed)
- [/] Persona lint as a CI gate (shadowing failures block merge)

---

## Phase 4 — Chat advanced

### Embedding tier ([03-stack.md](docs/03-stack.md#data))

- [/] Pick the embedding model — `all-MiniLM-L6-v2`, fp32 `onnx/model.onnx` (~90 MB), 384-dim, mean-pooled + L2-normalized. **Not quantized**: every published int8 variant of this class targets AVX2/AVX512/ARM64 and the J2900 is SSE4.2-only, so quantizing would be slower, not faster. Fetched by `scripts/fetch-model.sh` (gitignored, `MODEL_PATH` volume)
- [/] ONNX Runtime lazy singleton session in `Sonarr.Infrastructure` (`OnnxTextEmbedder`; missing model files are non-fatal — matching degrades to lexical-only)
- [~] Benchmark: 4.3 ms/embed on the dev box (i7, `IntraOpNumThreads = 2`), inside the 10–50 ms budget with headroom for the J2900 being several times slower. Re-measure on the J2900 itself at the September move
- [/] Semantic matcher: runs only when lexical score is below threshold; never overrides a confident lexical match (`EmbeddingSemanticMatcher`, floor 0.62, rescue score capped just over `LexicalThreshold`; a rescued intent still clears its own guards)
- [/] `chat.intent_embedding` cache keyed by content hash; rebuilt only when the persona file changes (`SemanticIntentIndex` + `SemanticWarmup`; unchanged examples cost nothing, removed ones are pruned)
- [/] `chat.episode.embedding vector(384)` + ivfflat index (`EpisodeConfiguration`, `vector_cosine_ops`, lists=100)
- [/] Backfill job for legacy episode embeddings (batch, minutes) — `EpisodeEmbedder`, 32 rows per batch. Not just legacy: the turn path writes episodes unembedded on purpose, so this is the only thing that ever fills the column
- [/] Callback retrieval: pgvector top-K episode, relevance-gated (`CallbackRetriever`, cosine ≥ 0.55, ≥ 8 turns old; the seeded odds are drawn *before* the lookup so the turns that would discard a tail never pay for the query)
- [/] Authored `examples:` on the intents most likely to be paraphrased (emotions, social, questions, hostility, memory-recall). Capture-driven intents (`SET_NAME`, `SET_FAV`) deliberately have none — a rescue carries no captures and their templates need them

### Relationship & memory features

- [/] Relationship tiers (stranger → … → inner_circle) with tier-gated guards in intents — six tiers in `sonarr.yaml`, thresholds pulled inside the `Registers.Min/Max` clamp (the old ladder topped out at `min_trust: 30`, which trust clamped at 20 could never reach). `persona/intents/tiers.yaml` is the first authored use of `min_tier`/`max_tier`: warm/cold pairs rather than guards on the originals, because the pinned-behavior tests match against `MatchContext.Empty` where `TierId` is null and `min_tier` fails closed
- [/] Authored tier-up moment — `ChatEngine.MarkTierChange` compares the derived tier before/after this turn's affect and appends a line from `tier_up_<id>`; once per tier per person via the persisted fired-log under a synthetic `tier:<id>` key, which is also what keeps demotions quiet
- [/] Assigned nicknames — tier-triggered, pool-drawn, stable per user, stored — drawn once at a tier crossing when no `name` slot exists, from `assigned_nickname`; surfaced to templates through `ConversationState.RenderSlots` rather than written into the slots payload, so the column stays the single home
- [x] `chat.fact` confidence: reinforce on repeat mention → hedging behavior when low — the reinforce half was already in `PersonRepository.UpsertFactAsync` (0.6 initial, +0.15 per repeat, a changed value supersedes rather than overwrites); hedging is `ReplyComposer.Hedged`, which wraps a shaky *value* in an authored `fact_hedge` line instead of pairing every recall pool with an unsure variant, so one pool covers every line that ever substitutes a remembered slot. The threshold (`ChatPipeline.HedgeBelowConfidence = 0.7`) sits between the initial value and one reinforcement, and the comparison happens in the adapter — the engine receives `TurnInput.ShakySlots`, a set of names, so nothing database-shaped crosses into `Sonarr.Elaine`. No slots means no query
- [x] `chat.relationship_event` trajectory queries → `#trend#` ("almost tolerable this week") — `ChatIntrospection.TrendAsync` sums the stored trust deltas over 7 days and appends `trend_up`/`trend_down` to the tier line, so "you're a regular" and "and it's getting worse" can both be true. Summed deltas rather than a level comparison because a delta is movement she caused — decay is not something you did. Below `TrendThreshold` (1.0, roughly one earned moment) the week is flat and gets no line: a sentence about no movement is worse than no sentence
- [/] Multi-day grudge decay — the mechanism was already in place (`ClockSignals.StepsPerHourAway = 1` → `TurnInput.ExtraDecaySteps` → `Registers.Decay(root, 1 + steps)`), so this was a matter of proving the numbers actually behave: with grudge at 0.02/turn a night away is noise, a week measurably cools it, and `MaxExtraDecaySteps = 720` (~30 days) keeps a year away from wiping a nemesis clean. Pinned at both levels — `ChatEngineTests` for the engine arithmetic, `ChatPipelineTests` for the clock wiring, since the pipeline is the only place a real elapsed time turns into a step count. `AbsenceTier` is still derived and unread; the "long time no see" pool it exists for is authored persona work, not code
- [/] Favorites / least-favorites, taking sides, rivalry commentary from trust ordering — `IPersonRepository.GetTrustRankedUsersAsync` orders one guild by trust and returns **ids only**, since another user's registers are theirs (docs/06); raw parameterised SQL casting `(registers->>'trust')::float8` because the column goes through the jsonb serialize-to-text converter and LINQ would otherwise load the whole guild to sort it. `ChatIntrospection.StandingAsync` appends `standing_top`/`standing_bottom` after the tier and trend lines, and only for the extremes — "you're fourth" is a leaderboard and she doesn't hand out numbers. That is also the only rivalry commentary that survives the privacy rule: the line is about *your* position and never names anyone else. `StandingThreshold = 3.0` means somebody she feels nothing about costs no query at all (pinned by `FakePersonRepository.TrustRankReads`), and `MinRanked = 2` because being first of one is not a ranking
- [/] `chat.stance` registry + `stance_agreement` — she remembers whose side you took. The schema, entities, EF config and migration all shipped in `20260727121712_Initial`, and the opinions themselves were already authored in `persona/stances.yaml`; what was missing was any code that read or wrote them, so this is wiring, not machinery. **The pool is the join key**: `stances.yaml` says pineapple_pizza is argued from `social_food` and the FOOD intent draws from `social_food`, so a turn that drew that pool put that opinion on the table — nothing is authored twice and a new topic intent inherits its stance for free (`PersonaGraph.StancesByPool`). "Which opinion did she just voice" is then answered by the already-persisted `FiredLog`: `ChatStances.Taken` looks only at intents whose fired turn equals the pre-turn `state.Turn`, i.e. her immediately previous reply, because agreeing two topics later is agreeing with whatever she said last. No new state, no new migration. `chat.stance` is upserted from the persona on the write path inside the caller's transaction rather than synced by a startup service — an agreement without its opinion is an FK violation, and a reworded opinion self-corrects the next time it comes up. Last word wins per topic: changing your mind replaces the row, since docs/04 says "whose side you took", singular. Affect is fondness ±(1.0/-0.5), not trust — siding with her is charming, not evidence about you, and the AGREE intent's own authored `affect:` block already covers plain agreement. Fell out of the audit: `joke_dad` had 20 authored lines and no intent that could fire them, so the `dad_jokes` stance was an opinion she could never be caught holding — new `DADJOKE` intent (regex-only; a bare `pun` keyword would eat half of JOKE), pinned by a test that every stance's pool is reachable
- [/] `/opinion` — embeds only the topic centroid of recent messages, discards it after; hits the stance registry. `ChatOpinions` takes the last 15 message texts (each cut at 300 chars — a wall of text is still one contribution), embeds them in one batch, averages and renormalises into a centroid, and scores it against the embedded stance topic labels (`topic.Replace('_', ' ')` *is* the query text, pinned by a test that every authored topic id reads as words). The centroid is a local that goes out of scope on return: nothing about the messages is stored, logged or cached, which is the whole metadata-safety claim from docs/10. The text has to be read at the Discord module layer because the Redis ring buffer is deliberately metadata-only (docs/05: author, timestamp, mentioned-her — no content), so `/opinion` calls `GetMessagesAsync` itself and hands the application layer a plain `IReadOnlyList<string>`, which also keeps `ChatOpinions` testable without Discord. `TopicFloor = 0.35`, well below `CallbackRetriever.RelevanceFloor = 0.55`, because this compares a whole conversation to a two-word label rather than sentence to sentence and the same subject scores lower. No match is not an error: she falls back to the already-authored-but-unreferenced `neutral_opinion` pool, which is a real answer, and the same fallback covers a missing model, a stance pointing at a nonexistent pool, and an empty channel. Seeded per topic, so her take on crypto is the same take every time you ask. First consumer of `GetStanceAgreementsAsync`: when your row for that topic exists, one of two new pools (`opinion_you_agreed`/`opinion_you_disagreed`) is appended — only *your* row is ever read, so the public reply cannot leak whose side anybody else took (docs/06). Public rather than ephemeral, since a take on the room is about the room; `DeferAsync` first because embedding a dozen messages is milliseconds on the dev box and uncomfortably close to Discord's 3 s window on the J2900
- [/] Server-event memory: `chat.guild_state.event_log` fed by `PresenceSampler` — `GuildEvent(Kind, Value, At)` and nothing else, pinned by a reflection test: this log is read out loud in a public channel, so a property that could carry a user id would break docs/06 the moment someone added one. The rules live in `GuildEventLog` (pure, in `Sonarr.Domain`) and the repository only loads the row and saves it, which is what makes the behaviour testable — the suite has no database-backed tests and adding one for a comparison would have been the expensive way to check arithmetic. `WithOnlineRecord` returns **null** rather than an unchanged array when the count was not beaten, so a normal sampler pass costs one indexed read and no write; the sampler already skips mid-reconnect, and a second `online <= 0` reject here keeps a zeroed member cache from becoming a record of nobody (which would also make every later sample a "new record"). The old record stays behind the new one — she can say what it was before — capped at 50 entries, because the record before the record before this one is not something she brings up. Malformed or foreign-shaped entries are skipped, not thrown over: a bad log must not cost her a reply. Kept as a jsonb array on the guild's one row rather than a table since it is a handful of entries per guild forever, read whole or not at all. Surfaced on `/serverinfo` as a "Busiest" field (`<t:…:R>`, so Discord renders the age in the reader's own locale) — the authored "last time this many people were online…" line is persona work, and the data it needs is now there
- [/] Mood of the day reseed + seasonal overlay activation — **not** via `DailyTick`, which would be the worse build: a scheduled reseed makes the reply depend on when the tick last ran, so a stored turn can't be replayed, a missed tick leaves her in yesterday, and a restart at 23:59 either double-fires or skips a day. Both signals are already derived from the turn's own instant in `ClockSignals.From` — `DaySeed` (the date, hashed, mixed into the salt) and `Overlays` (`OverlayActivation.Active` over hour/month/day/dow) — which is the same behaviour with no service, no state and no failure mode. What was actually broken was *whose* calendar: `ClockSignals` called `now.ToLocalTime()`, i.e. the container's TZ, so on a UTC container the 3–6 am overlay fired in the Saigon afternoon and the mood turned over mid-morning. Now `From` reads the offset it is handed and the pipeline converts to the **guild's** configured zone (`ConfigKeys.Timezone`, Redis-cached, so one dictionary-shaped read after the gates — an ambient message pays nothing). Guild and never the speaker: an overlay replaces a pool for the whole room, so following whoever is typing would put her in October for one person and not the next. An unset or unresolvable id falls back to UTC rather than throwing — same rule as `ZoneResolver`, and with `InvariantGlobalization` a dev box without tzdata can only ever get that one, which is why the zone-sensitive test asserts against `HasTzData` instead of skipping. `DailyTick`'s remaining duties (streaks, first-message-bonus reset, birthdays, anniversaries) are real scheduled work and stay open at line 314
- [/] Chat engine can recall `social.quote_board` entries — the table shipped in `20260727121712_Initial` with no repository, service or caller, so this is the first code that touches it. Reuses the existing callback seam (`TurnInput.Callback` → `ReplyComposer.Wrap`) rather than adding a second composition path, but **not** the existing pool: every `callback_tail` line claims the quote as hers ("i already told you: …", "my notes say …"), which is right for her own episode and a lie for a board row, where the whole point is who said it. So `TurnInput.Callback` became `RecalledQuote(Quote, Author?)` and a null author selects `callback_tail` while a present one selects the new `quote_board_tail`, whose ten authored lines all name `{$who}` (pinned by a test, because a single line that forgot the author would only misattribute on that one draw, in production). The author stays a raw mention id — resolving a display name would need a gateway lookup in the application layer, and Discord renders `<@id>` anyway; the reply already goes out with `AllowedMentions.None`, so quoting somebody does not ping them. Keyword overlap, not embeddings: a board is tens of rows per guild and its appeal is the exact wording, so `QuoteBoardRecall` reduces the message through the engine's own `Normalizer` to at most 6 content words of 4+ chars and the repository does one `ILIKE ANY` scan (a paraphrase match would surface a quote nobody connects to what was just said). Her own episodes win when both exist — an episode cleared a relevance floor a keyword match cannot promise. Guild-scoped with no exceptions, since a quote board is one server's inside joke; recalling it is only acceptable at all because docs/06 counts a saved quote as data its author consented to. Rides the existing 1-in-2 `WantsCallback` draw, so a turn that would discard a tail still pays for no lookup, and fails open like every other recall. `/quote save` and `/quote random` are the commands that fill the board and stay open at line 306
- [/] Retention: `chat.episode` capped at newest N per user (default 200) + anything referenced by a fact — `PruneAsync` had the cap but not the exception, and its own remark claimed the "referenced by a fact" clause had nothing to read. That was wrong: `chat.fact.learned_at_turn` and `chat.episode.turn` are the same logical clock for the same person, so `(guild_id, user_id, turn)` *is* the join even without an FK, and the clause is now a `NOT EXISTS` against it. Gated on `f.active`, because a superseded fact is kept for trajectory rather than recall and letting a dead one pin an episode forever would make the cap unenforceable — a person who changes their mind often would keep every episode they ever had. One statement, not a read-then-delete: the ranking and the exception belong in the same transaction or a turn landing mid-prune loses a row it just wrote. Driven by `RetentionPruner` at line 336

---

## Phase 5 — Web & community

### API (Kestrel :5088, [09-web-panels.md](docs/09-web-panels.md#api-surface-bot-process-kestrel-5088))

- [ ] ASP.NET Core minimal API hosted in the bot process, plain HTTP (tunnel terminates TLS)
- [ ] CORS locked to `sonarr.hault.io.vn`
- [ ] CSRF double-submit token required on every admin write
- [/] Migration — `web`: login_token, session, audit — landed in Migration 1
- [ ] `POST /api/auth/request-token` — always 202, neutral response (no user enumeration)
- [ ] Token: 8 chars, unambiguous alphabet (no 0/O/1/I), CSPRNG, SHA-256 hashed at rest, single-use, 10 min
- [ ] Rate limits: 3 requests / 15 min per **target username** and per **source IP** (`rl:login:*`)
- [ ] `LoginTokenSender` — DM the code, record the hash
- [ ] `POST /api/auth/verify` — max 5 attempts per token, then the token dies
- [ ] Session: 256-bit random id hashed at rest, cookie `HttpOnly; Secure; SameSite=Lax`, 24 h / 90 d remember-me, sliding renewal
- [ ] `POST /api/auth/logout` and `logout-all` ("log out everywhere")
- [ ] `web:session:{hash}` Redis mirror for fast validation; Postgres is the authority; revocation checked in DB on sensitive routes
- [ ] Admin routes: user id checked against the allow-list in Postgres per request, not the cached session
- [ ] `GET /api/me`, `/api/me/data`, `/api/me/export`
- [ ] `DELETE /api/me/chat-memory`, `/api/me/everything` (confirmation + cooling-off, honest note about backups)
- [ ] `GET /api/status` — public-safe blob, no auth
- [ ] `GET/PUT /api/admin/*` — config, flags, cases, stats, audit; writes go through the same Application services
- [ ] `web.audit` row for every panel action
- [ ] `StatusPagePusher` — `web:live_status` computed at most once per 5 s regardless of visitors

### Next.js panels (`web/`)

- [ ] App Router + TypeScript scaffold, dark theme, Sonarr branding only
- [ ] Server components for data pages (serve HTML, not a SPA megabyte)
- [ ] i18n-ready strings from day one (Vietnamese later)
- [ ] `/login` page + DM-token flow + help box explaining the DMs-open requirement
- [ ] User: Overview (level/XP/rank/streak, message count, join date, active reminders)
- [ ] User: My data — the transparency page, live, matching [06](docs/06-data-and-privacy.md) exactly
- [ ] User: Sonarr & me — relationship status, assigned nickname, per-fact "ask her to forget"
- [ ] User: My errors — recent errors from my own commands (replaces a Discord errors channel)
- [ ] User: Music — my history, my ratings, server top tracks
- [ ] User: Privacy actions — export JSON, delete chat memory, delete everything
- [ ] Admin: Status (gateway, Lavalink, DB/Redis, uptime, heartbeats, queues)
- [ ] Admin: Config editor with validation + export/import
- [ ] Admin: Feature flags per module per guild, instant
- [ ] Admin: Mod log browser (searchable `mod.case`)
- [ ] Admin: Stats — command usage, activity charts, member growth
- [ ] Admin: Audit — who changed what from the panel
- [ ] Admin panel must **not** expose other users' chat memories/facts — counts only

### Community features

- [/] Migration — `social`: quote_board, capsule, event, event_rsvp, ticket — landed in Migration 1
- [/] `/quote save` (+ context menu) / `/quote random` — `QuoteModule`: `Save quote` context menu on the outer class (a context command has no subcommand path, so it cannot sit inside the `[Group]`), `save`/`random`/`delete` in the nested `[Group("quote")]`. The board is one of docs/06's explicit-consent storage paths, so saving is always a command and never automatic. Confirmations are public, not ephemeral — an ephemeral reply would hide from the quoted person that their line was stored. `AllowedMentions.None` + `Format.Sanitize` everywhere, so being on the board is not a notification. `/quote delete` goes past the two commands asked for because docs/06 gives the quoted person the right to take their own words down; ownership (`SavedBy` or `AuthorId`) lives in the `WHERE` clause, so a guessed id matches nothing instead of removing somebody else's row. `random` uses `ORDER BY random()` — cheap on a board of tens of rows behind the `(guild_id, author_id)` index; ponytail marker records the `TABLESAMPLE` upgrade past a few thousand.
- [x] `/capsule write when message` — delivery via `core.job` — `CapsuleService` (Application, behind `ICapsuleService` because `ZoneResolver` is internal) + `CapsuleJobHandler` + `[Group("capsule")]`. Two rows per capsule: the text in `social.capsule`, the timing in `core.job` with a payload that carries only `capsule_id` + `user_id`. A reminder-style payload-only design would have been less code, but a capsule can sit for a year and docs/06 requires `/privacy` and forget-me to reach the text — a job payload is not a place anybody looks. The capsule row is written before the job, so the losing half is an unopened row somebody can still find rather than a job pointing at nothing that fails three times and wakes a human. Recurring phrases are refused (`/remind` is the repeating one) and anything inside an hour is refused too, since the parser's floor is seconds and that would make this a slow `/say`. Caps: 2048 chars (the column) and 10 unopened per person per guild — these are the only user-authored rows that outlive a membership. `delivered_at` is stamped **after** a successful post, not claimed before it: stamping first makes a failed send permanent, while the remaining narrow window only ever double-posts, and the `delivered_at IS NULL` clause in the `UPDATE` makes a late retry a no-op. Confirmation is ephemeral (a capsule is a surprise), and on open the author is the only mention.
- [x] `/event create|list|cancel` — native Discord events + opt-in role pings — `EventService` (behind `IEventService`, same internal-`ZoneResolver` reason as capsules) + `EventRepository` + `EventModule`. The commands split by audience rather than by noun: `/events` and the RSVP buttons sit on the outer class so anyone can read the schedule and answer, while `create`/`cancel` live in a nested `[Group("event")]` carrying `[DefaultMemberPermissions(ManageEvents)]`. That attribute is only a hint Discord can be told to ignore, so `cancel` re-checks `GuildPermissions.ManageEvents` itself and the service takes staffness as a flag — permissions are Discord's to answer, ownership is the row's. The native scheduled event is a best-effort mirror created *after* the RSVP post: if Discord refuses it (External events need an end time and a location, both invented here — channel name, +2h) the event still exists and the buttons still work, and the failure is an ephemeral followup rather than a lost command. `ping_role` is the opt-in half: going grants it, maybe/no take it back, and the module refuses `@everyone` and managed roles up front and checks its own hierarchy before touching anyone's roles. Recurring phrases are refused — a series needs its own cancel semantics and a native event per occurrence, which is not what was asked. Caps: 100/1000 chars (the columns, and Discord's title limit), 10 upcoming per creator per guild, 10 min minimum lead, a year maximum. Counts for `/events` come from one grouped query for the whole page rather than one per row.
- [ ] `/ticket` — private thread with mods; close button saves a transcript
- [x] `/ship user_a user_b` — deterministic seed from the id pair, no table — `ShipMeter` (Application) + `ShipModule`. FNV-1a over the *sorted* id pair, so `/ship a b` and `/ship b a` are the same question, and the answer never re-rolls — a changing number would make it obvious it means nothing. Guild-independent on purpose: two people are the same two people in every server they share, and a per-guild number invites shopping for a better one. The line is drawn seeded on the percentage rather than the turn, so the verdict is as stable as the arithmetic it claims to be. Three authored bands in `introspection.yaml` (`ship_low/mid/high`, third person — anyone can ship anyone). Public reply with `AllowedMentions.None`: the joke is for the room, but being shipped is not a notification. Nothing stored, no table, as specified.
- [ ] `/anniversary` — auto-announced join anniversaries + command shows yours
- [ ] Birthday feature (opt-in `core.member.birthday`, month+day)
- [ ] `SeasonRoller` — month boundary: close season, write results, announce top chatter, open next
- [ ] `DailyTick` — streaks, first-message-bonus reset, birthdays, anniversaries, mood reseed, overlay check
- [ ] Streak display in `/rank`

---

## Cross-cutting (must hold at every phase)

### Privacy & data ([06-data-and-privacy.md](docs/06-data-and-privacy.md))

- [ ] No general message-content logging — counts and timestamps only
- [ ] Content stored only when: you talk to Sonarr, you explicitly save it, or a mod action captures context
- [ ] Chat engine learns **only** from mention/reply — never mines the channel
- [ ] DM content never stored except the login token we sent (hashed)
- [ ] No third-party/outbound data sharing (list stays empty by design)
- [ ] `/privacy`, the panel "My data" page, and this doc must never disagree — single source
- [ ] Timezone + birthday are opt-in via command only
- [ ] Mod cases are not self-service deletable (audit integrity)
- [ ] User panel queries scoped to session `user_id` in **every** query
- [ ] Web app has no DB credentials and no Discord token

### Retention & maintenance

- [/] `RetentionPruner` (04:00): episode caps, dead login tokens/sessions, Redis orphans, stats > 400 d — a plain `BackgroundService` on a daily delay, **not** a `core.job` row like everything else in `JobScheduler`. Durability is the only thing a job row buys, and a missed sweep costs nothing here: every cutoff is computed from "now", so the next run deletes the same rows plus a day's worth. `UntilNextRun` treats 04:00 exactly as *tomorrow*, or a sweep finishing inside the same minute would immediately run again. The episode cap and the row sweep sit in separate try blocks: the cap is the half that keeps the pgvector table searchable, and a failing stats delete must not skip it. Four `ExecuteDeleteAsync` calls rather than one — deliberately not a single transaction, so one wedged table does not roll back the other three, and each filters on an already-indexed column (`expires_at` on both web tables, `hour_bucket`, `day`). **Redis needs no sweep at all**: every key the bot writes carries a TTL, so docs/08's "expired Redis orphans" is handled by construction, which is what line 338's audit exists to keep true. `SweepAsync` is public so the tests drive a sweep without waiting for 04:00 — same pattern as `JobScheduler.PollOnceAsync`
- [/] `stats.activity_sample` aggregated hourly, kept 400 days — the aggregation was already structural rather than a job: the table keys on `(guild_id, hour_bucket)` and `PresenceSampler` truncates its 5-minute sample to the hour, so the twelve samples in an hour upsert one row instead of growing the table. That is the cheap half of "aggregated" and it needs no reduce step. The 400-day half is the pruner at line 336, deleting on the indexed `hour_bucket`; at one row per guild per hour, 400 days is under 10 k rows per guild, which is why the window can be that generous
- [x] Redis audit: every key has a TTL; nothing durable lives only in Redis — enforced two ways, neither of them a document someone has to remember to re-read. `RedisCacheBase.WriteAsync` takes the expiry as a **required** parameter and throws on a non-positive one, so a key cannot be written without a TTL at all; and `RedisKeysTests` now reflects over both types in both directions — every builder in `RedisKeys` must have a same-named `CacheTtl` entry, and every `CacheTtl` entry must belong to a live builder. The second direction matters as much as the first: a TTL nobody reads is a policy that has drifted from the key it claims to govern, which is exactly how a key ends up written with an arbitrary literal at the call site. "Nothing durable" is the reference-direction rule already pinned by `ArchitectureTests` plus docs/05's own list — facts, episodes, relationships and jobs are Postgres-only, and `core.job` is the authority precisely so the scheduler survives a flush
- [x] `FLUSHALL` blast-radius test: costs only reset conversations, one music session, panel re-logins — every key family now declares which of docs/05's three permitted costs it is, and a second reflection test fails if any family has no declaration. That pairing is the whole point: asserting the three costs alone would pass forever while somebody quietly adds a fourth. The classifications that needed a decision rather than a glance: the rate limiters are a *reset*, not data loss, since a flush only forgives whoever was mid-window — and `rl:login` fails **closed** while Redis is down, so a flush can never open the DM-token gate; `presence:*` is a reset because `PresenceSampler` re-samples within 5 minutes and `stats.activity_sample` already holds the durable copy; `cfg:*` is a reset because a miss falls through to the guild's Postgres row, costing one uncached read. `web:session` is a mirror of the `web.session` table, so the worst case is the re-login docs/05 already allows

### Backups & ops ([11-deployment.md](docs/11-deployment.md#backups-user-decision-files-on-host-foldered-by-yearmonth))

- [ ] `BackupRunner` 03:30 — `pg_dump -Fc | gzip` → `/root/backups/sonarr/YYYY/MM/sonarr-YYYY-MM-DD.dump.gz`
- [ ] Dump sanity check (non-zero, header check); failure = red line in the log channel
- [ ] Weekly config backup: persona dir + `.env`, `config-` prefix, same tree
- [ ] Retention: dailies 30 days, then first-of-month kept 12 months
- [ ] Restore drill documented in-repo; tested once before cutover, then quarterly
- [ ] Document the single-disk caveat; weekly `rsync` to the old CT once the J2900 lands
- [ ] Never touch `lavalink.service`, the yt-cipher container, or `/root/lavalink/*`

### CI (nice-to-have, phase 5)

- [ ] GitHub Actions: build + test; engine behavior catalog + persona lint are the gate
- [ ] Publish images to GHCR
- [ ] Deploy = `ssh compose pull && up -d`
- [ ] Interim: `deploy/` script doing the same by hand

### September J2900 move

- [ ] Install Debian + Docker; copy the compose dir and backup tree
- [ ] Move Lavalink + yt-cipher first (same versions, same `application.yml`), verify from the running CT bot
- [ ] `pg_dump` on CT → restore on J2900 → `docker compose up` → repoint the tunnel
- [ ] Demote the CT to backup-rsync target
- [ ] Re-verify the RAM budget on real hardware (~1.6–2.2 GB expected)

---

## Guardrails — do not build these

Economy (wallet/bank/daily/work/pay) · shop/titles/prestige/consumables · achievements ·
all gambling and board games · the adventure/dungeon RPG · `!` prefix commands ·
command chaining · the custom help system · sleep-mode gating · any generative-AI call ·
anything that can't run on the J2900.

