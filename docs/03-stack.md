# 03 — Technology Stack

Every choice, with the reason and the rejected alternative. Constraint stamps:
🐢 = must run on J2900 (no AVX, 4 threads, 8GB shared) · 🔒 = no generative AI.

## Runtime & bot

| Choice | Version | Why | Rejected |
|---|---|---|---|
| .NET | 10 (LTS) | Target platform per project goal; async-first fixes the old bot's blocking-SQLite-on-event-loop disease | staying on Python |
| Discord library | **Discord.Net 3.20.x** | Stable, targets net10.0, mature interactions framework, biggest ecosystem | NetCord (still beta), DSharpPlus (stable is netstandard2.0; net10 build is nightly-only) |
| Music client | **Lavalink4NET 4.2.x** (`.Discord.Net` package) | Targets net10.0, actively maintained, proper voice-state handling (fixes the VC-drag crash the old bot hand-patched), native filter support | Victoria (less active), hand-rolled REST |
| Lavalink server | **4.2.2 — the existing install, untouched** | Verified latest release (2026-07); proven working with youtube-plugin 1.18.1 + yt-cipher | upgrading for its own sake |
| Scheduling | built-in `BackgroundService` + Postgres-backed job table | reminders/tempbans must survive restarts; a jobs table + one poller is enough at this scale 🐢 | Quartz.NET/Hangfire (heavier than the need) |

## Data

| Choice | Version | Why | Rejected |
|---|---|---|---|
| Database | **PostgreSQL 17 + pgvector** | Relational memory model for chat (facts/episodes/relationship events), vector search for semantic recall, one DB for everything | keeping SQLite (no vectors, file-locking pain with web panel) |
| DB access | **EF Core 10** (+ raw SQL where hot) | Migrations, LINQ for the 95% of queries that are simple; pgvector via `pgvector-dotnet` | Dapper-only (more boilerplate for CRUD-heavy schema) |
| Cache / transient state | **Redis 7** | TTL'd session state, cooldowns, snapshots, rate limits — survives bot restarts, dies on purpose via TTL | in-memory (dies with process; user explicitly wants Redis) |
| Embeddings | **ONNX Runtime + small embedding model** (bge-small / nomic-embed class, quantized) | Semantic understanding + recall with ~10–50ms CPU inference 🐢🔒; runs in-process, no service to babysit | local LLM (rejected: too slow on available hardware), cloud API (rejected: user wants none) |

## Web

| Choice | Why | Rejected |
|---|---|---|
| **Next.js** (user's pick) — App Router, TypeScript | User preference; SSR keeps the user panel light; one framework for both panels | Blazor (would keep it all-.NET but user chose Next.js — fine, the API boundary is clean either way) |
| API: **ASP.NET Core minimal API in the bot process**, port 5088 | Panels need live bot state; in-process = no IPC layer (see 02) | separate API service |
| Auth: **DM-token login** (user's design) + long-lived session cookie for "remember me" | No OAuth app setup; proves control of the Discord account; details in [09-web-panels.md](09-web-panels.md) | Discord OAuth (more moving parts; can be added later without breaking anything) |
| HTTPS | terminated by the user's tunnel/proxy for `sonarr.hault.io.vn`; we serve plain HTTP | managing certs ourselves |

## Observability & ops

| Choice | Why |
|---|---|
| **Serilog** — console (structured, for `docker logs`) + rolling file | The "mod actions on CLI" requirement = structured console sink; files for archaeology |
| Health: startup self-test + `/status` + panel status page | verifies Postgres/Redis/Lavalink/Discord perms on boot, posts one green/red line |
| Metrics: lightweight counters exposed on the API (`/api/metrics`) | commands/min, errors, Lavalink state — panel renders them; no Prometheus stack on 8GB 🐢 |
| **Docker Compose** — bot, web, postgres, redis | matches existing server practice; music stack intentionally outside |
| Backups: nightly `pg_dump` to **`/root/backups/sonarr/YYYY/MM/`** (host bind mount) + retention pruning | user decision: plain files on host, foldered by year/month |
| Tests: xUnit + behavior catalog for the chat engine | old `tests/test_logical_response.py` mined as the conformance spec |

## Sizing sanity check (J2900, 8GB)

| Component | Expected RSS |
|---|---|
| sonarr-bot (.NET, incl. ONNX model ~100–200MB) | 350–500MB |
| sonarr-web (Next.js) | 150–250MB |
| PostgreSQL | 150–300MB |
| Redis | 30–80MB |
| Lavalink (JVM) | 700MB–1GB |
| yt-cipher | ~100MB |
| **Total** | **~1.6–2.2GB** — comfortable in 8GB with OS + page cache |

CPU: everything is I/O-bound except embedding inference (rare path, tens of ms) and
Lavalink transcoding (its own proven workload). 4 threads suffice.
