# Sonarr

A Discord bot for one community: authored personality chat with long-term memory,
a full Lavalink music player, levels, moderation, and a web panel. This repo is the
greenfield **C# / .NET 10** rewrite of a Python bot that worked but lost its state on
every restart.

No LLM anywhere. The personality is authored and deterministic; embeddings are used
for understanding and recall only.

## Layout

Two processes (docs/02-architecture.md):

- **sonarr-bot** — one .NET 10 process: Discord gateway (Discord.Net), chat engine
  (`Sonarr.Elaine`), music (Lavalink4NET), background services, and the panel REST API
  on Kestrel `:5088`.
- **sonarr-web** — Next.js, the only Node process. Serves both panels, holds no state,
  talks to the bot's API. Not built yet (phase 5).

Lavalink and yt-cipher stay on the host as they are (systemd + container). We never
touch them.

```
src/Sonarr.Domain          entities, domain models, IService/IRepository contracts
src/Sonarr.Elaine          chat engine — pure logic, no Discord/DB/HTTP
src/Sonarr.Application     service implementations, one folder per module
src/Sonarr.Infrastructure  repositories, SonarrDbContext, Redis, ONNX, Lavalink wiring
src/Sonarr.Bot             host: gateway, interaction modules, API controllers
src/Sonarr.Migrator        one-shot: old SQLite/JSON -> Postgres
tests/                     xUnit — engine behavior catalog + service tests
deploy/                    prod compose skeleton, deploy script, restore drill
_bot_legacy/               the retired Python bot (see below)
```

## Running the dev stack

```sh
cp .env.example .env          # fill in DISCORD_TOKEN, POSTGRES_PASSWORD, PG_CONNECTION
docker compose up -d          # postgres:17+pgvector and redis:7 on 127.0.0.1
dotnet run --project src/Sonarr.Bot
```

Postgres and Redis are bound to loopback only. The bot itself runs on the host in dev
(hot reload, debugger); it is containerised in prod — `src/Sonarr.Bot/Dockerfile`,
built from the repo root.

Set `DISCORD_DEV_GUILD_ID` so slash commands register per-guild and appear instantly.

```sh
dotnet test                   # engine behavior catalog is the regression floor
```

## Docs

| Doc | Contents |
|---|---|
| [docs/01-overview.md](docs/01-overview.md) | What Sonarr is, naming rules, phases, out of scope |
| [docs/02-architecture.md](docs/02-architecture.md) | Process layout, layering convention, project structure |
| [docs/03-stack.md](docs/03-stack.md) | Every technology choice and why |
| [docs/04-database.md](docs/04-database.md) | PostgreSQL schema |
| [docs/05-caching.md](docs/05-caching.md) | Redis keys, TTLs |
| [docs/06-data-and-privacy.md](docs/06-data-and-privacy.md) | What we collect, retention, deletion |
| [docs/07-commands.md](docs/07-commands.md) | All slash commands |
| [docs/08-background-services.md](docs/08-background-services.md) | Everything running besides commands |
| [docs/09-web-panels.md](docs/09-web-panels.md) | Next.js panels + DM-token login |
| [docs/10-elaine-engine.md](docs/10-elaine-engine.md) | The chat engine |
| [docs/11-deployment.md](docs/11-deployment.md) | Docker, servers, backups, cutover |
| [docs/checklist.md](docs/checklist.md) | Build checklist, phase by phase |

`what user need.md` is the feature contract. `deploy/RESTORE.md` is the backup restore
drill — run it before cutover, then quarterly.

## The legacy Python bot

`_bot_legacy/` is the retired bot, kept on purpose:

- **Reference implementation** — the persona YAML, matcher behavior, and music quirks it
  hand-patched are the spec for their .NET replacements.
- **Conformance suite** — `_bot_legacy/tests/test_logical_response.py` is mined into the
  engine behavior catalog. Every pinned behavior there has to survive the rewrite.

It is not built, not deployed, and gets deleted only after cutover plus the rollback
window.

## Ground rules

These gate every change (docs/README.md):

1. **Users only ever see "Sonarr."** Elaine is the internal name of the chat module; it
   never appears in a command, reply, or the web UI.
2. **No generative AI.** Every reply is authored.
3. **Everything runs on the target box** — Pentium J2900, 4C/4T, 8GB RAM, no AVX.
   Anything that can't gets redesigned, not excused.
4. **Nothing durable is lost on restart.** State is Postgres (durable), Redis (transient
   by design, TTL'd), or explicitly documented as ephemeral.

Secrets live in `.env`, which is gitignored and never committed.
