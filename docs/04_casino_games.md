# 04 — The Casino (cogs/games/)

The `Games` cog is a grab-bag of gambling and PvP minigames, all gated to a
configured `games_channel` via `cog_check`. The `__init__.py` is the command
surface (442 lines); each game lives in its own file and exposes a
`start_<game>(cog, ctx, ...)` entry. An `active_games` set prevents a user
from being in two games at once.

Games present: coinflip (PvE + PvP), blackjack, roulette, crash, mines,
high-low, duel, tic-tac-toe, connect-four, heist, arena, word-chain, rob.

## The Mines cashout-button saga (a monument)

This is the single funniest artifact in the codebase, and it's *left in the
source as comments*. In `mines.py`, the dev tries to add a "Cash Out" button
to a 5×5 minefield, then realizes mid-code that Discord caps a view at 25
buttons — and a 5×5 grid is exactly 25:

```python
self.cashout_btn = discord.ui.Button(label="💰 Cash Out", ...)  # ... wait
# Ah! A 5x5 grid takes all 25 slots. Where does the Cashout button go?
# Maybe we should use a 5x4 grid = 20 buttons, or we just instruct them to
# type `cashout`?
# ...
# We can just check messages for "cashout".
```

So `self.cashout_btn` is **constructed and never added to the view**. It's
dead. The actual cashout mechanism is `start_mines` spinning a
`bot.wait_for('message')` loop watching for someone to literally type
`cashout` in chat ([mines.py:194](../cogs/games/mines.py#L194)). It works, but
the UI promises a button it never shows, and the reasoning is preserved as a
wall of confused comments. Peak hobby-code honesty.

### Mines also has a math problem

`calculate_mines_multiplier` already bakes in a 3% house edge per tile
(`fair_multiplier * (1 - house_edge)`). But `reveal_safe` then *compounds*
that per-tile multiplier (`self.multiplier *= next_mult`), so the house edge
is applied multiplicatively on every reveal. The deeper you go, the more the
edge stacks against you beyond the stated 3%. The payout curve is harsher
than the "fair" framing suggests.

## Roulette: the docs lie

`roulette.py` accepts `high`/`low`/`1st`/`2nd`/`3rd` (dozens) plus
red/black/odd/even and straight numbers. But the user-facing docstring in
`games/__init__.py` advertises `1-18`/`19-36` as bet types — and those strings
aren't in `valid_choices`, so the documented bets just bounce with "Invalid
choice." Classic doc/impl drift where the doc is the one that's wrong.

## Arena: PvE in name only

`arena.py`'s docstring calls it "PvE ... rock-paper-scissors." It is neither.
It's a PvP matchmaking brawler with attack/defend/special, counters, special
misses, and interrupt logic ([arena.py:16](../cogs/games/arena.py#L16) is the
round resolver, and it's actually a decent little combat system). Two
problems: the description is fiction, and players queued waiting for an
opponent aren't added to `active_games`, so they can wander into another game
while queued.

## Rob: numbers don't match the label

`rob.py`'s docstring says "2-hour cooldown / steal 10–30%." The code uses
14400s (**4 hours**) and steals **15–25%**. Both numbers are wrong in the
docs. The behavior is fine; the contract is stale.

## The pattern across all of these

The games *work*. The bugs are almost entirely **doc-vs-code drift** and one
genuinely-stuck UI decision (Mines). Nobody's losing money to a crash; they're
losing it to a docstring that describes a different game. These are catalogued
in memory as the next cleanup candidates — each is an isolated single-file
fix, and the docstring in `games/__init__.py` is the contract to fix *toward*.
