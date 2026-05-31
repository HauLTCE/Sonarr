# 01 — Architecture & The Entry Point

## The big picture

`main.py` boots a single `commands.Bot` instance with the `!` prefix. It loads
cogs, wires up a custom help command, a spam gate, command logging, and a
Lavalink connection for music. Everything runs in one process, one event loop.

```
main.py
 ├─ load_extensions()        # discovers + loads cogs
 ├─ on_message              # &&-chaining + spam gate (before commands run)
 ├─ before_invoke           # per-command-type spam check + logging
 ├─ on_command_error        # fuzzy "did you mean?", cooldowns, perms
 └─ cleanup_message_cache   # daily tasks.loop for cache + spam pruning
```

## Cog loading — two kinds of cog

`load_extensions()` ([main.py:361](../main.py#L361)) is more clever than the
average bot's. It supports:

- **Package cogs**: a hardcoded list `['sonarr_ai', 'music', 'games',
  'adventure']`, each a directory with an `__init__.py`.
- **File cogs**: every `*.py` in `cogs/` *except* an `ignored_files`
  blocklist (`views.py`, `sonarr_ai.py`, `music.py`) — files that were
  superseded by packages but left on disk.

Then it loads them **concurrently** with `asyncio.gather`. That's a nice
touch — model-heavy cogs (the AI) load in the background while the rest come
up. The catch: `gather(..., return_exceptions=True)` means a cog that fails
to import is logged and silently skipped. The bot will happily come online
missing a feature, and you find out when someone runs the command.

## The `&&` command chain — the most ambitious thing in main.py

`on_message` ([main.py:242](../main.py#L242)) intercepts every message and, if
it starts with `!`, splits on `&&` so users can chain commands:

```
!daily && !work && !balance
```

Each segment is processed separately by copying the message object
(`copy.copy(message)`), rewriting `.content`, and re-dispatching. Along the
way it does real shell-style tokenizing with `shlex.split`, partial-name
resolution via `_find_command_by_prefix`, and — if a prefix is ambiguous —
it actually **prompts the user** with a numbered menu and waits 30 seconds
for a reply (`bot.wait_for`). For a hobby bot, that's a lot of UX.

It's also where a real bug once lived: `_find_command_by_prefix` was *called
but never defined*, so any `&&` chain raised `NameError` — silently swallowed.
It now exists ([main.py:218](../main.py#L218)) and short-circuits exact
matches so a full command name never reads as ambiguous against a longer one.

## Spam runs in two places

There are **two** spam checks on different layers:

- `check_spam` in `on_message` — rate of *all* commands (chain-aware: a
  3-command chain counts as 3).
- `check_command_type_spam` in `before_invoke` — rate of *one specific*
  command.

Both share the same global `command_counts` dict in `utils/spam.py`, which
(spoiler for `07`) stores two completely different value shapes under the
same keys. It works, but it's a trap.

## Error handling

`on_command_error` ([main.py:132](../main.py#L132)) is genuinely good. It
handles wrong-channel (for music), restricted-hours, missing args, cooldowns,
not-owner, and — the nice part — `CommandNotFound` triggers `difflib`-based
fuzzy suggestions ("Did you mean `!balance`?"). Everything else falls to a
`logger.critical` with a generic user-facing message. Sensible.

## Where it's fragile

- Cog list is partly hardcoded (`package_cogs`) and partly
  discovery-with-a-blocklist. Add a new package cog and forget the list →
  it silently doesn't load.
- The `on_message` override means the AI cog *and* main.py both listen to
  every message. Ordering and double-processing are things you have to keep
  in your head.
- Lavalink connection retries up to 8 times on `on_ready`; if it never
  connects, `bot.lavalink_ready` stays False and music commands degrade.
