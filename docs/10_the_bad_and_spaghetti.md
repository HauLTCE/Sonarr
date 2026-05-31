# 10 — The Bad & The Spaghetti

The honest ledger, roughly worst-to-least. None of this stops the bot from
running — that's the recurring theme. It's "works despite," not "works
because."

## Tier S — actually dangerous

**1. Root SSH password committed in plaintext.** `deploy.py` and
`boost_player.py` both hardcode `USER='root'`, `PASSWORD='12345'`. It's in git
history now. Small blast radius today (private box, Tailscale IP), catastrophic
the day that stops being true. This is the only finding that's a genuine
*security* problem rather than a tidiness one.

**2. One shared SQLite cursor across a multi-threaded process.** Every helper
hits `db.cursor` directly; `check_same_thread=False` + the ML/executor threads
means concurrent `execute`/`fetch` on one non-thread-safe cursor. Latent data
corruption that's been masked by SQLite's write serialization and low traffic.
Knowingly left alone because the fix is a real refactor. (See `06`.)

## Tier A — load-bearing weirdness

**3. The overloaded `command_counts` dict** stores two different value shapes
under one global, kept apart only by int-vs-str key type, requiring
`isinstance` sniffing to clean up. A "tidy" refactor that normalizes the keys
will crash the bot. (See `07`.)

**4. The phantom Postgres backend.** A fully-built `PostgresWrapper` + HA
branch that imports `database.postgres`, a module that does not exist. Set
`HA_ENABLED=true` and you get an ImportError caught into a SQLite fallback.
Elaborate dead scaffolding. (See `06`.)

**5. Two parsers for one effects DSL.** `effects.py` parses the prefix
language properly; `selector.py._strip_effects` re-parses it by hand with
`split(":")` and doesn't know about half the prefixes. Two sources of truth
that already disagree on edge cases. (See `02`.)

## Tier B — the spaghetti

**6. main.py does too much.** The entry point owns command chaining, fuzzy
suggestions, a spam gate, command logging, *and* an interactive disambiguation
prompt inside `on_message`. `_process_segment` is a 75-line closure with
nested `wait_for` flows. It works and it's even thoughtful, but it's a lot of
control flow for a file whose job is "start the bot."

**7. The Mines dead button.** `self.cashout_btn` is built, never added,
and surrounded by a wall of the dev arguing with themselves in comments. The
real cashout is a chat `wait_for('cashout')`. Funny, but it's confusion left
fossilized in source. (See `04`.)

**8. Doc-vs-code drift in the games.** Roulette advertises bets it rejects;
arena calls itself PvE rock-paper-scissors while being a PvP brawler; rob's
cooldown/percent numbers are both wrong in the docstring. The *contract* lies,
not the behavior. (See `04`.)

**9. Mines compounds the house edge.** The per-tile multiplier already
includes a 3% edge, then gets multiplied cumulatively, stacking the edge
beyond the stated rate. A math bug hiding inside a working game loop.

## Tier C — papercuts & smells

- **Cog loading is half-hardcoded, half-discovered** with a `ignored_files`
  blocklist of superseded files (`views.py`, `sonarr_ai.py`, `music.py`) left
  on disk. Add a package cog, forget the list, it silently won't load.
- **Stale deploy targets.** `deploy.py`/`boost_player.py` point at
  `/root/sonarr/` and `100.108.202.81`, both pre-Docker. They no longer match
  the live `192.168.1.101` Docker layout. Legacy scripts masquerading as
  current ones.
- **f-string SQL in `boost_player.py`** — the one place not using
  parameterized queries. Dev-constant values only, so not exploitable, but
  it's the wrong pattern sitting next to code that does it right everywhere
  else.
- **`scripts/fix_*.py`** exist because NLP data deps and Lavalink break often
  enough to need repair scripts. Not a bug, but a tell about dependency pain.
- **Silent excepts.** Several `try/except: pass` / bare `except:` blocks
  (command logging, message deletes). Harmless individually; collectively they
  mean failures vanish without a trace.
- **The whole AI is heavy.** PyTorch + 3 transformers + spaCy + NLTK + BM25 in
  memory to pattern-match insults. Defensible as a learning project, absurd as
  engineering. (See `02`.)

## The pattern

Notice the split: the **dangerous** items (S) and the **dead/duplicated** items
(A) are all in the *foundation and tooling* — the parts nobody plays with. The
*spaghetti* (B/C) is in the surface features that got built fast and shipped.
The well-built parts (`09`) are the ones that got a second pass. This codebase
is a near-perfect map of where attention was spent and where it wasn't.
