# Modularization + Shared-Cursor Plan (NEEDS YOUR SIGN-OFF)

These are the two high-risk items from your task list. I did **not** start them
— they can break a running bot in non-obvious ways, and you can't supervise
right now. This is the plan; approve/adjust when you're well and I'll execute
in small reversible commits.

## A. The shared SQLite cursor (the real foundation risk)

**Problem:** `utils/database.py` opens ONE connection with `check_same_thread=
False` and shares ONE `self.cursor` across the whole bot. Every helper does
`db.cursor.execute(...)`. The ML inference pool, the async-bridge executor, and
the daily cleanup loop are all separate threads. Two threads interleaving
`execute`/`fetchone` on one cursor can read each other's rows. SQLite
serializes *writes*, which is why it hasn't visibly exploded — but it's a
latent corruption bug, not a theoretical one.

**Why I won't rush it:** it's the single highest-blast-radius change in the
repo. Every cog reads `db.cursor`. A wrong move corrupts economy/dungeon data.

**Proposed approach (incremental, each step shippable):**
1. Add a `db.execute(sql, params)` / `db.query(...)` / `db.query_one(...)` API
   that uses a **short-lived cursor per call** under a `threading.Lock` (or
   `connection.execute`, which makes its own cursor). Connection stays shared;
   only the cursor stops being shared. Keep `db.cursor` working so nothing
   breaks yet.
2. Migrate call sites cog-by-cog to the new API (one commit per cog, testable
   in isolation). ~10 files.
3. Once nothing uses `db.cursor` directly, remove it.
**Risk if skipped:** rare, hard-to-reproduce data races under concurrency.
**Recommendation:** do it AFTER you're back, with the bot stopped for the
cutover commit. Not a sick-day solo job.

## B. Modularization ("so it lives no matter what")

Good news: the bot already self-heals on boot (idempotent schema, guarded
migrations, `asyncio.gather(..., return_exceptions=True)` so one bad cog is
logged and skipped). The dungeon (`cogs/adventure/`) is already the model:
thin cog + engine + items + views + data-in-JSON.

**Highest-value, low-risk modularization targets:**
1. **`main.py` (408 lines) is doing too much** — the `&&` chain handler, fuzzy
   suggestions, and spam gate could move to `utils/command_router.py` and
   `utils/suggestions.py`. Pure extraction, no behavior change. Safe, and makes
   `on_message` readable. **I can do this one now if you want** (it's
   extract-and-import, fully reversible).
2. **The cog loader's hardcoded `package_cogs` list** — replace with discovery
   (any `cogs/<dir>/__init__.py` is a package cog). Removes the "forgot to add
   it to the list → silently doesn't load" trap.
3. **`utils/database.py` (643 lines)** — split the schema-creation DDL into
   `utils/schema.py` so the class is just the access layer. Cosmetic but big
   readability win. Pairs naturally with item A.

**What I'd NOT do:** a big-bang restructure. The architecture is fine; it needs
targeted extraction, not a rewrite.

## My ask
Tell me which of A1/B1/B2/B3 you want me to start. B1 and B2 I can do safely
today; A I'd rather schedule with you watching. Everything stays on this branch,
one commit each.
