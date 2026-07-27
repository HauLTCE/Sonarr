# 02 — Architecture

## Process layout: modular monolith + separate web frontend

Two application processes, by design (see "Why not microservices" below):

```
┌────────────────────────────── J2900 / current CT ──────────────────────────────┐
│                                                                                │
│  sonarr-bot (one .NET 10 process)          sonarr-web (Next.js container)      │
│  ├─ Discord gateway (Discord.Net)          └─ user panel + admin panel         │
│  ├─ Chat engine (Sonarr.Elaine)                 │ HTTPS via user's tunnel      │
│  ├─ Music (Lavalink4NET client)                 ▼                              │
│  ├─ Background services (08-…)             calls REST API ──► sonarr-bot :5088 │
│  └─ ASP.NET Core minimal API  ◄─────────────────────────────────┘              │
│                                                                                │
│  postgres:17 (+pgvector)   redis:7   lavalink (systemd, UNTOUCHED)   yt-cipher │
└────────────────────────────────────────────────────────────────────────────────┘
```

- **sonarr-bot** hosts the Discord client, all bot logic, all background services,
  and the REST API (Kestrel, port 5088, plain HTTP — HTTPS is terminated by the
  user's tunnel/proxy in front of `sonarr.hault.io.vn`).
- **sonarr-web** is the only Node process: Next.js serving both panels, talking to
  the bot's API. It holds no state and no database access.
- **Lavalink + yt-cipher stay exactly as they are** (systemd + container on host).
  The music stack is proven-fragile; we never touch it.

## Why not microservices (decision record)

Considered and rejected for the bot itself:

- **RAM is the scarce resource** on the J2900 (8GB total; Lavalink ~1GB, Postgres +
  Redis + Next.js + yt-cipher ~1GB more). Each extra .NET service ≈ 150MB + duplicated
  connection pools.
- **The API needs live bot state** (current queue, gateway latency, Lavalink health,
  chat-engine state). In-process that's a method call; across services it's a
  pub/sub + cache-sync problem we'd build and debug for zero scale benefit at ~14
  active users.
- **The seams are kept anyway** (see layering below): every module is split-ready.
  If a real reason appears (e.g. the chat engine needs isolation), extraction is
  mechanical, not archaeological.

## Layering convention (per module, strict)

Adopted per user decision — every feature module follows:

```
Controller  →  IService  →  Service  →  IRepository  →  Repository  →  DbContext
```

- **Controller** — two flavors, same layer: Discord *interaction modules*
  (slash/component handlers) and *API controllers* (REST for the panels). Both are
  thin translators: parse input → call service → format response. No logic.
- **IService / Service** — all business rules. Services never see Discord types or
  HTTP types; they take/return domain models. This is what makes one feature usable
  from both Discord and the web panel without duplication.
- **IRepository / Repository** — all data access. Services never touch `DbContext`
  or Redis directly. Repositories return domain models, not EF entities, where they
  differ.
- **DbContext** — one EF Core context (`SonarrDbContext`), pgvector-enabled.
  Redis sits behind repository-shaped interfaces too (e.g. `ISessionCache`).

Rules: interfaces live with the domain, implementations with infrastructure;
cross-module calls go through the other module's `IService`, never its repository.

## Solution structure

```
Sonarr.sln
├── src/
│   ├── Sonarr.Domain            # entities, domain models, interface definitions
│   │                            # (IService/IRepository contracts) — no dependencies
│   ├── Sonarr.Elaine            # chat engine: pure logic, persona loader/validator,
│   │                            # matchers, affect, grammar. No Discord/DB/HTTP refs.
│   ├── Sonarr.Application       # Service implementations (business logic),
│   │                            # per module: Music/, Levels/, Moderation/, Chat/,
│   │                            # Config/, Reminders/, Social/, Auth/…
│   ├── Sonarr.Infrastructure    # Repository implementations, SonarrDbContext,
│   │                            # Redis, embeddings (ONNX), Lavalink4NET wiring,
│   │                            # Discord REST helpers
│   ├── Sonarr.Bot               # HOST: Generic Host + Discord.Net gateway,
│   │                            # interaction modules (Discord controllers),
│   │                            # background services, ASP.NET Core API controllers
│   └── Sonarr.Migrator          # one-shot console app: old SQLite/JSON → Postgres
├── web/                         # Next.js app (sonarr-web)
├── tests/
│   ├── Sonarr.Elaine.Tests      # behavior catalog + engine unit tests
│   └── Sonarr.Application.Tests # service tests with fake repositories
└── docs/
```

Dependency direction: `Bot → Application → Domain ← Infrastructure` (classic clean
architecture; `Elaine` is referenced by Application like any other domain library).

## Cross-cutting

- **DI everywhere** — Generic Host container; every interface registered once in
  `Sonarr.Bot/DependencyInjection/`.
- **Kill switches** — every module checks `IFeatureGate` (backed by config +
  admin-panel toggle) at its Controller layer; a disabled module answers
  "this feature is currently off" rather than not existing.
- **Errors** — controllers catch, log full detail (Serilog, structured), show the
  user a friendly one-liner + case id. Stack traces never reach Discord or the panel.
- **Determinism boundary** — `Sonarr.Elaine` takes injected time/RNG; nothing inside
  it reads clocks, environment, or network. All I/O happens in Application services
  around the engine call.
