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

- [ ] Relationship tiers (stranger → … → inner_circle) with tier-gated guards in intents
- [ ] Authored tier-up moment
- [ ] Assigned nicknames — tier-triggered, pool-drawn, stable per user, stored
- [ ] `chat.fact` confidence: reinforce on repeat mention → hedging behavior when low
- [ ] `chat.relationship_event` trajectory queries → `#trend#` ("almost tolerable this week")
- [ ] Multi-day grudge decay
- [ ] Favorites / least-favorites, taking sides, rivalry commentary from trust ordering
- [ ] `chat.stance` registry + `stance_agreement` — she remembers whose side you took
- [ ] `/opinion` — embeds only the topic centroid of recent messages, discards it after; hits the stance registry
- [ ] Server-event memory: `chat.guild_state.event_log` fed by `PresenceSampler`
- [ ] Mood of the day reseed + seasonal overlay activation via `DailyTick`
- [ ] Chat engine can recall `social.quote_board` entries
- [ ] Retention: `chat.episode` capped at newest N per user (default 200) + anything referenced by a fact

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
- [ ] `/quote save` (+ context menu) / `/quote random`
- [ ] `/capsule write when message` — delivery via `core.job`
- [ ] `/event create|list|cancel` — native Discord events + opt-in role pings
- [ ] `/ticket` — private thread with mods; close button saves a transcript
- [ ] `/ship user_a user_b` — deterministic seed from the id pair, no table
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

- [ ] `RetentionPruner` (04:00): episode caps, dead login tokens/sessions, Redis orphans, stats > 400 d
- [ ] `stats.activity_sample` aggregated hourly, kept 400 days
- [ ] Redis audit: every key has a TTL; nothing durable lives only in Redis
- [ ] `FLUSHALL` blast-radius test: costs only reset conversations, one music session, panel re-logins

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

