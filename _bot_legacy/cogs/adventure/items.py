"""
Dungeon Item System — Items loaded from items_data.json.
Items are stored in adventure_inventory table.
"""
import random
import json
import os
from utils.database import db

# ========== ITEM CATALOG ==========
_ITEMS_PATH = os.path.join(os.path.dirname(__file__), "items_data.json")
with open(_ITEMS_PATH, "r", encoding="utf-8") as _f:
    ITEMS = json.load(_f)

# Rarity drop weights by floor range
RARITY_WEIGHTS = {
    "common":    {"min_floor": 1,  "max_floor": 15, "weight": 60},
    "uncommon":  {"min_floor": 5,  "max_floor": 30, "weight": 30},
    "rare":      {"min_floor": 10, "max_floor": 50, "weight": 15},
    "epic":      {"min_floor": 20, "max_floor": 80, "weight": 5},
    "legendary": {"min_floor": 35, "max_floor": 999, "weight": 1},
}

RARITY_COLORS = {
    "common": "⬜",
    "uncommon": "🟩",
    "rare": "🟦",
    "epic": "🟪",
    "legendary": "🟧",
}

RARITY_DISPLAY = {
    "common": "Common",
    "uncommon": "Uncommon",
    "rare": "Rare",
    "epic": "Epic",
    "legendary": "LEGENDARY",
}

GEAR_SELL_PRICES = {
    "common": 10,
    "uncommon": 30,
    "rare": 80,
    "epic": 200,
    "legendary": 500,
}

ALL_SLOTS = ["weapon", "helmet", "armor", "ring", "accessory"]

def get_item(item_id: str) -> dict:
    """Get item data from catalog."""
    return ITEMS.get(item_id)


def roll_loot(floor: int, is_boss: bool = False) -> str | None:
    """Roll for a loot drop based on floor. Returns item_id or None."""
    # Base drop chance: 25% normal, 80% boss
    drop_chance = 0.80 if is_boss else 0.25
    if random.random() > drop_chance:
        return None

    # Determine eligible rarities (enforce min_floor AND max_floor)
    eligible = []
    for rarity, info in RARITY_WEIGHTS.items():
        if info["min_floor"] <= floor <= info["max_floor"]:
            # Legendary only from bosses
            if rarity == "legendary" and not is_boss:
                continue
            eligible.append((rarity, info["weight"]))

    if not eligible:
        # Fallback: if floor is beyond all max_floors, use the two highest tiers
        eligible = [("epic", 5), ("legendary", 1)] if is_boss else [("epic", 5)]

    # Weight-based rarity selection
    rarities, weights = zip(*eligible)
    chosen_rarity = random.choices(rarities, weights=weights, k=1)[0]

    # Pick a random item of that rarity
    candidates = [iid for iid, data in ITEMS.items() if data["rarity"] == chosen_rarity]
    if not candidates:
        return None

    return random.choice(candidates)


RARITY_TIER = {"common": 0, "uncommon": 1, "rare": 2, "epic": 3, "legendary": 4}

def give_item(user_id: int, item_id: str) -> tuple[int, bool]:
    """Give an item to a user. Auto-equips if slot is empty or new item is higher rarity.
    Returns (inventory_row_id, was_auto_equipped)."""
    item = ITEMS[item_id]
    uid = str(user_id)
    slot = item["slot"]
    new_tier = RARITY_TIER.get(item["rarity"], 0)

    # Check what's currently equipped in that slot
    db.cursor.execute(
        "SELECT id, rarity FROM adventure_inventory WHERE user_id = ? AND slot = ? AND equipped = 1 LIMIT 1",
        (uid, slot)
    )
    current = db.cursor.fetchone()

    if current is None:
        # Empty slot → auto equip
        should_equip = True
    else:
        current_tier = RARITY_TIER.get(current[1], 0)
        # Only auto-equip if strictly higher rarity
        should_equip = new_tier > current_tier

    if should_equip and current is not None:
        # Unequip old item first
        db.cursor.execute("UPDATE adventure_inventory SET equipped = 0 WHERE id = ?", (current[0],))

    db.cursor.execute(
        "INSERT INTO adventure_inventory (user_id, item_id, slot, rarity, stats_json, equipped, quantity) VALUES (?, ?, ?, ?, ?, ?, 1)",
        (uid, item_id, slot, item["rarity"], json.dumps(item["stats"]), 1 if should_equip else 0)
    )
    db.connection.commit()
    return db.cursor.lastrowid, should_equip


def get_inventory(user_id: int) -> list:
    """Get all items for a user."""
    uid = str(user_id)
    db.cursor.execute(
        "SELECT id, item_id, slot, rarity, equipped FROM adventure_inventory WHERE user_id = ? ORDER BY equipped DESC, rarity DESC",
        (uid,)
    )
    rows = db.cursor.fetchall()
    result = []
    for row in rows:
        item_data = ITEMS.get(row[1])
        if item_data:
            result.append({
                "inv_id": row[0],
                "item_id": row[1],
                "slot": row[2],
                "rarity": row[3],
                "equipped": bool(row[4]),
                "name": item_data["name"],
                "emoji": item_data["emoji"],
            })
    return result


def equip_item(user_id: int, item_id: str) -> tuple[bool, str]:
    """Equip an item, unequipping any existing item in that slot. Returns (success, message)."""
    uid = str(user_id)
    item_data = ITEMS.get(item_id)
    if not item_data:
        return False, "Item not found."

    # Check user owns it
    db.cursor.execute(
        "SELECT id FROM adventure_inventory WHERE user_id = ? AND item_id = ? LIMIT 1",
        (uid, item_id)
    )
    row = db.cursor.fetchone()
    if not row:
        return False, "You don't own this item."

    target_slot = item_data["slot"]

    # Unequip any current item in that slot
    db.cursor.execute(
        "UPDATE adventure_inventory SET equipped = 0 WHERE user_id = ? AND slot = ? AND equipped = 1",
        (uid, target_slot)
    )

    # Equip the new item
    db.cursor.execute(
        "UPDATE adventure_inventory SET equipped = 1 WHERE id = ?",
        (row[0],)
    )
    db.connection.commit()
    return True, f"Equipped **{item_data['name']}** in {target_slot} slot."


def get_equipped_items(user_id: int) -> dict:
    """Get all equipped items as {slot: item_data_with_special_effects}."""
    uid = str(user_id)
    db.cursor.execute(
        "SELECT item_id, slot FROM adventure_inventory WHERE user_id = ? AND equipped = 1",
        (uid,)
    )
    equipped = {}
    for row in db.cursor.fetchall():
        item_data = ITEMS.get(row[0])
        if item_data:
            equipped[row[1]] = {**item_data, "item_id": row[0]}
    return equipped


def get_equipped_bonuses(user_id: int) -> dict:
    """Calculate total stat bonuses from all equipped items. Also returns special effects."""
    equipped = get_equipped_items(user_id)
    bonuses = {
        "attack": 0, "defense": 0, "speed": 0, "luck": 0, "max_hp": 0,
        # Special effects
        "lifesteal": 0.0,
        "bleed_on_hit": 0,
        "crit_chance_bonus": 0.0,
        "crit_damage_bonus": 0.0,
        "magic_dodge": 0.0,
        "self_damage_pct": 0.0,
        "damage_bonus": 0.0,
        "execute_chance": 0.0,
        "on_kill_stat_chance": 0.0,
        "on_kill_stat_chance_demon": 0.0,
    }

    for slot, item in equipped.items():
        for stat, val in item.get("stats", {}).items():
            if stat in bonuses:
                bonuses[stat] += val

        # Accumulate special effects
        for effect_key in ["lifesteal", "bleed_on_hit", "crit_chance_bonus", "crit_damage_bonus",
                           "magic_dodge", "self_damage_pct", "damage_bonus", "execute_chance",
                           "on_kill_stat_chance", "on_kill_stat_chance_demon"]:
            if effect_key in item:
                bonuses[effect_key] += item[effect_key]

    return bonuses


def remove_item(user_id: int, inv_id: int) -> bool:
    """Remove an item from inventory by its inventory row ID. Returns True if removed."""
    uid = str(user_id)
    db.cursor.execute(
        "DELETE FROM adventure_inventory WHERE id = ? AND user_id = ? AND equipped = 0",
        (inv_id, uid)
    )
    deleted = db.cursor.rowcount > 0
    db.connection.commit()
    return deleted
