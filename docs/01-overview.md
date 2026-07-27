# 01 — Overview & Concept

## What Sonarr is

Sonarr is a single-server-first Discord bot (works multi-guild, tuned for one community)
with four pillars:

1. **Personality chat** — mention/reply to Sonarr and get an instant, in-character
   (rude, begrudgingly fond) response from a deterministic authored engine with real
   long-term memory of each user. No LLM. See [10-elaine-engine.md](10-elaine-engine.md).
2. **Music** — full-featured Lavalink player: queue, playlists, filters, ratings,
   history, crash-resumable sessions.
3. **Community systems** — levels/XP (text + voice), streaks, seasons, moderation with
   an audit trail, welcome/autorole, events, tickets.
4. **Web presence** — `sonarr.hault.io.vn`: a user panel (see your own data, errors,
   stats) and an admin panel (config, mod log, kill switches). DM-token login.

## The concept in one paragraph

The old Python bot proved the personality concept but felt "stupid" (keyword-only
matching), carried a huge unwanted economy/gambling codebase, and lost state on every
restart. The rewrite keeps the two beloved things — the personality (with its authored
voice and per-user memory) and the music experience — deletes the economy entirely,
and rebuilds everything else as boring, durable, observable .NET services. Intelligence
comes from structure (scored matching, semantic embeddings, relational memory), not
from a language model.

## Naming rules

- The bot's name is **Sonarr** — in commands, replies, embeds, web UI, everywhere.
- **Elaine** is the internal codename of the chat engine only (project
  `Sonarr.Elaine`, persona files, this documentation). Users never see it.
- Command names are top-level and plain: `/memories`, `/relationship`, `/opinion` —
  not `/elaine memories`.

## Scope source of truth

[`../what user need.md`](../what%20user%20need.md) is the feature contract
(~35 small / ~45 medium / ~5 large items). Build phases:

| Phase | Delivers | Old bot |
|---|---|---|
| 1. Foundation | Solution scaffold, Postgres/Redis/Docker, config system, slash core, kill switches, self-test, `/checkperms` | still running |
| 2. Parity+ | Music (incl. VC-drag fix), levels, moderation + log, welcome/autorole, reminders, data migration | **retired** |
| 3. Chat core | New engine, persona migration, behavior-catalog tests, hot reload | — |
| 4. Chat advanced | Embedding tier, relationship tiers, memory features, personality extras | — |
| 5. Web & community | Next.js panels, stats, seasons, ship/streaks/anniversaries, tickets | — |

## Explicitly out of scope (never rebuilt)

Economy (wallet/bank/daily/work/pay), shop/titles/prestige/consumables, achievements,
all gambling and board games, the adventure/dungeon RPG, `!` prefix commands, command
chaining, the custom help system, sleep-mode gating.
