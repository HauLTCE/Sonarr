# 07 — Spam Guard & Command Chaining (utils/spam.py)

The spam system is small (127 lines) but it's a perfect little case study in
how a quick fix becomes a trap. It works. It's also the file most likely to
make a future maintainer say "wait, what?"

## Two layers of throttling

There are two independent checks, each with its own thresholds:

- **`check_spam(user_id, chain_count)`** — global command rate. >10 commands
  in a 10s window → 5-minute cooldown. Chain-aware: a `!a && !b && !c` chain
  counts as 3 against the window, which is the right call (it's called from
  `on_message` with `len(parts)`).
- **`check_command_type_spam(user_id, command_name)`** — per-command rate.
  >50 uses of *one* command in 300s → 10-minute timeout. Called from
  `before_invoke`. There's a `SPAM_EXEMPT_COMMANDS` set so music/help/ping/echo
  don't get throttled (you skip a lot, you don't want a cooldown).

The design intent is sound: cheap global flood protection plus per-command
abuse protection, with an exemption list for the chatty-but-harmless commands.

## The trap: one dict, two shapes

Both functions share the module-global `command_counts`. But they store
**completely different value types** under it:

```python
# check_spam stores a LIST OF TUPLES, keyed by int user_id:
command_counts[user_id] = [(timestamp, chain_count), ...]

# check_command_type_spam stores a DICT OF LISTS, keyed by str user_id:
command_counts[user_key] = { command_name: [timestamp, ...] }
```

The only thing keeping these from colliding is that one keys by `user_id`
(int) and the other by `user_key = f"{user_id}"` (str), and in Python
`594 != "594"` as dict keys. So they *don't* collide — by accident of type,
not by design. Nothing documents this. Nothing enforces it. If someone
"cleans up" by normalizing the key to `str(user_id)` in both, the two shapes
crash into each other and `.append` blows up on the wrong type.

The cleanup function `cleanup_spam_data` ([spam.py:94](../utils/spam.py#L94))
even has to **type-sniff** to prune the dict:

```python
if isinstance(val, dict):    # it's the per-command shape
    ...
elif isinstance(val, list):  # it's the global shape
    ...
```

That `isinstance` branch is the tell. Any time your cleanup has to inspect the
runtime type to know what it's looking at, your data model has two things
wearing one name.

## The memory-leak fix

Originally `command_counts` only ever grew — every user who ever ran a command
left an entry forever. `cleanup_spam_data` now prunes stale entries (and
`user_cooldowns` / `command_type_spam` expired ones) and is wired into the
daily `cleanup_message_cache` loop in `main.py`. So the leak is plugged, but
the plug had to be written *around* the dual-shape dict, which is why it's the
gnarliest-looking function in an otherwise tiny file.

## How it should have been

Two dicts. `global_counts: dict[int, list]` and
`per_command_counts: dict[str, dict[str, list]]`. No shared name, no
type-sniffing, no accidental-key-type safety net. It's a five-minute refactor
that nobody's done because the current version *works* and touching shared
mutable spam state on a live bot is exactly the kind of "why is everyone in
timeout now" risk you don't take casually.

## Verdict

A working, well-intentioned throttle whose state model is a loaded gun with
the safety taped on. Fine in place; do not "tidy" it without splitting the
dict first.
