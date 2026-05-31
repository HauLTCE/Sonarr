# 06 — The Database (utils/database.py)

`utils/database.py` is the foundation everything else stands on. It's a
765-line hand-rolled SQLite layer. It is simultaneously one of the more
*thoughtful* files (good PRAGMAs, real migrations) and the source of the
project's deepest architectural risk (one shared cursor for the whole bot).

## What it does well

- **WAL mode + sane PRAGMAs** ([database.py:52](../utils/database.py#L52)):
  `journal_mode=WAL`, `synchronous=NORMAL`, `cache_size=10000`. For a
  read-heavy bot with occasional writes, this is the correct tuning. WAL lets
  readers not block the writer.
- **Idempotent schema** — every table is `CREATE TABLE IF NOT EXISTS`, so a
  fresh DB self-initializes on boot. No separate migration tool needed.
- **Real, guarded migrations** — when columns were added after launch
  (`message_cache.guild_id`, `message_cache.words_json`,
  `adventure_character.dungeon_level/xp`), the code checks
  `PRAGMA table_info` (or catches `OperationalError`) and `ALTER TABLE`s only
  if missing. This is exactly how you evolve a live SQLite schema without
  losing user data. Genuinely good.
- **Sensible tables** — economy, levels, guild_config, achievements,
  gambling_stats, adventure_character/inventory, consumable_inventory,
  message_cache/response_cache (the AI's memoization), misgendering_memory,
  title_roles. Composite primary keys where they belong (`(user_id, guild_id)`).

## The big one: a single shared cursor

```python
self.connection = sqlite3.connect(..., check_same_thread=False, ...)
self.cursor = self.connection.cursor()   # ONE cursor, shared by everything
```

Every helper in the entire codebase does `db.cursor.execute(...)` against this
*same* cursor object. With `check_same_thread=False`, SQLite won't stop you
from touching it from multiple threads — and this bot has threads: the ML
inference pool, the `_run_async_in_thread` executor, the daily cleanup loop.

A cursor is **not** thread-safe. Two code paths interleaving
`execute`/`fetchone` on the same cursor can read each other's result rows.
SQLite serializes the *writes* at the connection level, which is why this
hasn't visibly exploded — but the fetch interleaving is a latent
correctness bug, not a theoretical one. It's documented in memory as
"architectural, risky" and deliberately left alone, because fixing it properly
(cursor-per-operation, or a connection pool) is a real refactor with its own
risk of breaking working behavior. That's a defensible call for a hobby bot;
it would not be at scale.

## The Postgres "HA mode" that does not exist

This is the best vaporware in the repo. At the top:

```python
USE_POSTGRES = os.getenv('HA_ENABLED', ...) or os.getenv('DATABASE_URL')
```

There's a whole `PostgresWrapper` with lazy init, a background event loop, a
30s timeout, and a SQLite fallback. `_create_database()` branches on
`USE_POSTGRES` and returns the wrapper "in HA mode." It looks production-grade.

But the actual implementation imports:

```python
from database.postgres import PostgresDatabase
```

**That module does not exist anywhere in the repository.** There is no
`database/postgres.py`, no `database/` package at all (the only thing close is
`core/database/__init__.py`, which just re-exports the SQLite `db` singleton).
So if anyone ever set `HA_ENABLED=true`, `_try_init` would throw `ImportError`,
log "PostgreSQL init failed... falling back to SQLite," and quietly run SQLite
anyway. The entire HA path is an elaborate no-op wrapped around a missing file.
It's harmless (the fallback catches it) but it's pure dead scaffolding — a
feature that was sketched, wired up, and never built.

## The async-from-sync bridge

`_run_async_in_thread` ([database.py:22](../utils/database.py#L22)) spins up a
*new event loop in a thread pool* to run async DB methods from sync code, with
a 30s `future.result(timeout=...)`. This exists to bridge the sync helper API
to the (nonexistent) async Postgres backend. For SQLite it's mostly unused
machinery. It's the kind of thing that's load-bearing only in a world that
never shipped.

## Bottom line

The schema management is better than most hobby bots. The concurrency model
is the project's biggest unexploded ordnance. And there's a fully-decorated
Postgres door that opens onto a brick wall.
