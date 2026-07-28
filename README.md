# Sonarr

A Discord bot for one community: authored personality chat with long-term memory, a full
Lavalink music player, levels, moderation, and a web panel. This is the **C# / .NET 10**
rewrite of a Python bot that worked but lost its state on every restart.

**No LLM anywhere.** Every reply Sonarr sends was written by a person and picked by
deterministic rules. Embeddings are used for understanding and recall — matching what you
said to an authored intent, remembering what you told it three months ago — never for
generation. That constraint is the point of the project, not a limitation of it: the bot
has a fixed personality that cannot drift, cost nothing per message, and runs on a
ten-year-old Pentium.

## What it does

| | |
|---|---|
| **Chat** | Mention it and it answers in character. Sentiment and mood tracking, per-user long-term memory, episode recall over pgvector, and a behavior catalog that pins every authored response so a refactor can't quietly change its personality. |
| **Music** | Lavalink4NET against a Lavalink 4.2.2 node: queue, playlists, filters, now-playing, per-guild stats. Handles being dragged between voice channels, which the old bot hand-patched around. |
| **Levels** | XP with anti-spam cooldowns, streaks, role rewards, leaderboards. |
| **Moderation** | Warn/mute/kick/ban with a case log, tempbans that survive restarts, purge, audit trail, a permission preflight (`/checkperms`), and private mod threads via `/ticket`. |
| **Utility** | Reminders, birthdays, events with RSVP, time capsules, quote board, milestones, `/ship`. |
| **Panel** | Next.js: a user side (your data, your stats, music queue) and an admin side (cases, config editor, feature flags, audit, stats). Login is a token DM'd by the bot — no passwords, no OAuth redirect. |
| **Privacy** | `/privacy` and a My Data page: see what's stored, export it, delete it. No general message-content logging — counts and timestamps only, enforced by a test that reflects over every column in the schema. |

97 slash commands (85 distinct names — the rest are subcommands like `list` and `set` reused
across groups) across 28 interaction modules.

## Layout

Two processes:

- **sonarr-bot** — one .NET 10 process: Discord gateway (Discord.Net), chat engine
  (`Sonarr.Elaine`), music (Lavalink4NET), background services, and the panel REST API on
  Kestrel `:5088`.
- **sonarr-web** — Next.js, the only Node process. Serves both panels, holds no state,
  talks to the bot's API.

Plus Postgres 17 (+pgvector), Redis 7, Lavalink and yt-cipher — all compose services in
one stack.

```
src/Sonarr.Domain          entities, domain models, IService/IRepository contracts
src/Sonarr.Elaine          chat engine — pure logic, no Discord/DB/HTTP
src/Sonarr.Application     service implementations, one folder per module
src/Sonarr.Infrastructure  repositories, SonarrDbContext, Redis, ONNX, Lavalink wiring
src/Sonarr.Bot             host: gateway, interaction modules, API controllers
src/Sonarr.Migrator        one-shot: old SQLite/JSON -> Postgres
tests/                     xUnit — engine behavior catalog + service tests
deploy/                    prod compose stack, deploy script, restore drill
web/                       the Next.js panel (user + admin)
```

Dependencies point one way: `Bot → Application → Domain ← Infrastructure`. `Sonarr.Elaine`
depends on nothing but the BCL, so the chat engine is testable without a Discord token or a
database — `ArchitectureTests` fails the build if either rule is broken.

## Running the dev stack

```sh
cp .env.example .env          # fill in DISCORD_TOKEN, POSTGRES_PASSWORD, PG_CONNECTION
docker compose up -d          # postgres:17+pgvector and redis:7 on 127.0.0.1
dotnet run --project src/Sonarr.Bot
```

Postgres and Redis are bound to loopback only. The bot runs on the host in dev (hot reload,
debugger) and containerised in prod — `src/Sonarr.Bot/Dockerfile`, built from the repo root.

Set `DISCORD_DEV_GUILD_ID` so slash commands register per-guild and appear instantly instead
of waiting on Discord's global propagation.

```sh
dotnet test                   # the engine behavior catalog is the regression floor
```

Deploying is `deploy/deploy.sh` (`--build` on the box, or `--pull` from GHCR). CI builds,
tests, typechecks the panel and publishes both images. `deploy/RESTORE.md` is the backup
restore drill — run it before trusting a backup, then quarterly.

## The legacy Python bot

The retired Python bot used to live in `_bot_legacy/`. It is gone from the working tree;
everything it was kept for has been extracted:

- **Reference implementation** — its persona YAML now lives in `persona/`, loaded directly
  by the engine rather than copied.
- **Conformance suite** — `tests/Sonarr.Elaine.Tests/BehaviorCatalogTests.cs` is the mined
  catalog, restated against the v2 engine. Every behavior pinned there still has to survive,
  and the ones with no v2 route are recorded as comments rather than dropped.

The last commit containing the tree is the `python-bot-final` tag, so provenance comments in
the C# stay resolvable:

```sh
git show python-bot-final:_bot_legacy/tests/test_logical_response.py
```

## Ground rules

These gate every change:

1. **Users only ever see "Sonarr."** Elaine is the internal name of the chat module; it
   never appears in a command, a reply, or the web UI.
2. **No generative AI.** Every reply is authored.
3. **Everything runs on the target box** — Pentium J2900, 4C/4T, 8GB RAM, no AVX. Anything
   that can't gets redesigned, not excused.
4. **Nothing durable is lost on restart.** State is Postgres (durable), Redis (transient by
   design, TTL'd), or explicitly documented as ephemeral.

Secrets live in `.env`, which is gitignored and never committed; `.env.example` carries the
keys with empty values.

## A note on the docs

The design notes and build checklist that drove this rewrite live in `docs/` on disk but are
deliberately not published — they carry server layout, guild ids and operational detail that
does no good in a public repo. The code is the specification here: decisions are recorded as
comments where the decision lives, and every non-obvious trade-off has one.

This is a bot for a single community rather than a product you deploy. It is public because
the engine, the layering and the no-LLM approach might be useful to read, not because it is
built for reuse — there is no multi-tenant story and there won't be one.
