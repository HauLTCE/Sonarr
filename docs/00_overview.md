# SONARR — A Field Guide to a Random Discord Bot

This is the documentation nobody asked for, about a Discord bot nobody planned.
It covers the good, the bad, the weird, the spaghetti, and the occasional
moment of genuine cleverness. It is written honestly. If something is held
together with tape, this guide says so.

## What is this thing?

SONARR is a single-process Python Discord bot that tries to be roughly five
products at once:

1. **An AI personality** — a sassy NLP-driven chatbot ("queen", not "bro")
   that classifies what you say using *three* transformer models and replies
   from a library of hand-written canned responses.
2. **A casino** — blackjack, roulette, mines, crash, coinflip, high-low,
   plus PvP duels, connect-four, tic-tac-toe, heists, and an arena.
3. **An economy** — wallet, bank, gems, daily streaks, work, rob, titles,
   prestige, and a shop that openly insults you for being poor.
4. **A roguelike dungeon** — floors, enemies, bosses, gear with rarities,
   consumables, bleed stacks, crit math, and a leveling curve.
5. **A music bot** — Lavalink/wavelink playback. This part is *off-limits*.
   It is fragile, it works, and touching it has historically broken it for
   days. We document around it, never into it.

On top of all that: moderation commands, leveling/XP, welcome messages,
autorole, a utility cog, and a spam guard.

## The shape of the codebase

- ~19,000 lines of Python across ~119 files.
- `main.py` is the entry point and does a surprising amount of work itself
  (command chaining with `&&`, fuzzy "did you mean?" suggestions, a spam
  gate, command logging).
- Cogs come in two flavors: plain file cogs (`cogs/economy.py`) and package
  cogs (`cogs/adventure/`, `cogs/games/`, `cogs/music/`, `cogs/sonarr_ai/`).
- The AI lives in its own `sonarr/` tree, separate from `cogs/`, with an
  input-check pipeline and a giant folder of response files (47 in
  `sonarr/responses/main/` alone).
- Persistence is SQLite (`bot_data.db`) via a hand-rolled `utils/database.py`
  with WAL mode and a single shared cursor.
- Deployment is a pair of `paramiko` SSH scripts that nuke-and-reupload to a
  server, and (more recently) a Docker container.

## How to read this guide

Each file covers one subsystem and is candid about its state:

- `01_architecture.md` — entry point, cog loading, the `&&` chain handler.
- `02_the_ai_brain.md` — the three-model classifier, effects, sleep mode.
- `03_economy_money_integrity.md` — the TOCTOU race and how it got fixed.
- `04_casino_games.md` — games, and the famous Mines cashout-button saga.
- `05_the_dungeon.md` — the adventure engine and the text/interaction adapter.
- `06_the_database.md` — the shared cursor, migrations, and the Postgres
  feature that doesn't exist.
- `07_spam_and_chaining.md` — the overloaded `command_counts` dict.
- `08_deployment_and_scripts.md` — SSH scripts, hardcoded passwords, the
  player-boost cheat tool.
- `09_the_good.md` — what's genuinely well done.
- `10_the_bad_and_spaghetti.md` — the messes, ranked.
- `what-i-think.md` — the honest verdict.

## One-line summary

It is an ambitious, over-scoped, frequently-clever, occasionally-cursed
hobby bot that does far more than it has any right to — and mostly works.
