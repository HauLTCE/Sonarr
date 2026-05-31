# 05 — The Dungeon (cogs/adventure/)

If `02` is the most over-engineered subsystem and `04` is the funniest, the
dungeon is the **best-organized** code in the entire project. It's a
text-based roguelike with floors, scaling enemies, bosses, gear, rarities,
consumables, and a leveling curve — and it's split cleanly:

```
cogs/adventure/
 ├─ __init__.py            # thin cog: wires commands → pieces
 ├─ engine.py             # mechanics: enemies, damage, XP, scaling
 ├─ items.py              # gear catalog, rarity drops, equip logic
 ├─ consumables_logic.py  # potion/buff handling
 ├─ views.py              # combat + menu UI (buttons)
 ├─ text_adapter.py       # makes !commands reuse the button UI
 └─ guide.py              # in-game help embeds
```

The module docstring literally states the separation of concerns ("The cog is
intentionally thin"), and the code honors it. Data (enemies, items) lives in
JSON files loaded at import. This is how the rest of the bot *wishes* it were
structured.

## The combat math is real

`engine.py` has actual systems, not just `random.randint`:

- **Diminishing stat scaling** (`_scale_stat`): linear growth per floor up to
  a soft cap (50), then logarithmic for the overflow. Keeps floor-9000
  enemies from having astronomical stats.
- **Damage** (`calculate_damage`): `max(1, atk - def)`, ±15% variance, crit
  chance from luck (`min(0.75, luck/100 + bonus)`), crit multiplier, plus
  item-driven damage bonuses. Supports gear synergies.
- **Bleed stacks**, **lifesteal**, **magic dodge**, **on-kill stat gain** —
  the Blackened Sword has a chance to permanently grant +1 to a random stat
  on a demon kill, and there's type-tagging (`is_demon`) so items can have
  matchup bonuses.
- **Dungeon leveling** (`award_dungeon_xp`): separate from the global level
  system. Each level grants +5 max HP and +1 random stat, with proper
  multi-level-up-in-one-gain handling.

## Gear & rarity

`items.py` loads a catalog from `items_data.json` with five rarities
(common→legendary), floor-gated drop weights, color/emoji mapping, and sell
prices. Legendaries are floor-35+ with weight 1 — genuinely rare. Equip logic,
inventory, and `get_equipped_bonuses` (which the cog folds into effective
stats) all live here.

## The clever bit: the text→interaction adapter

The combat UI is built as Discord button views (`CombatView`,
`AdventureView`). But the bot also supports `!attack` / `!flee` / `!explore`
as text commands. Rather than duplicate the combat logic, `text_adapter.py`
fakes the `discord.Interaction` surface that the views expect:

- `SimpleContext` — a minimal ctx-like object.
- `_TextResponse` — stands in for `interaction.response` (its `send_message`
  even respects an `ephemeral` flag by auto-deleting after 8s, since text
  channels have no ephemeral messages).
- `TextInteraction` — adapts a `commands.Context` to the slice of
  `discord.Interaction` the views use, editing a tracked "screen message"
  instead of spawning new ones.

This is a legitimately good design decision: one combat implementation, two
input modalities. It's the kind of abstraction the rest of the codebase
avoids — and here it actually earns its keep.

## What bit it before (and got fixed)

- **Prestige used to wipe `deepest_floor`.** Prestiging (an economy action)
  was clobbering dungeon progress. Fixed so deepest floor survives.
- The `dungeon_level` / `dungeon_xp` columns were added later, so
  `database.py` has explicit `ALTER TABLE` migrations guarding them (see
  `06`).

## The remaining smell

`engine.py` reaches straight into the shared `db.cursor` for every character
read/write (`get_character`, `update_character`), same as everything else.
The dungeon's cleanliness is at the *module* level; at the *data* level it
shares the same single-cursor fate as the rest of the bot. For a turn-based,
one-player-at-a-time dungeon that's low-risk, but it's the one place the nice
architecture leaks into the messy foundation.
