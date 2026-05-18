"""
Dungeon Item System — 42 items across 5 tiers + 7 Legendary weapons/accessories.
Items are stored in adventure_inventory table.
"""
import random
import json
from utils.database import db

# ========== ITEM CATALOG ==========

ITEMS = {
    # ===== COMMON (Floors 1-15, 60% drop) =====
    "rusty_sword": {
        "name": "Rusty Sword", "slot": "weapon", "rarity": "common", "emoji": "🗡️",
        "description": "A dull blade. Better than your fists.",
        "stats": {"attack": 3},
    },
    "wooden_shield": {
        "name": "Wooden Shield", "slot": "armor", "rarity": "common", "emoji": "🛡️",
        "description": "Barely holds together, but it blocks something.",
        "stats": {"defense": 3},
    },
    "cloth_armor": {
        "name": "Cloth Armor", "slot": "armor", "rarity": "common", "emoji": "👕",
        "description": "It's cloth. Don't expect miracles.",
        "stats": {"defense": 2, "speed": 1},
    },
    "leather_boots": {
        "name": "Leather Boots", "slot": "accessory", "rarity": "common", "emoji": "👢",
        "description": "Run slightly faster from your problems.",
        "stats": {"speed": 3},
    },
    "herb_pouch": {
        "name": "Herb Pouch", "slot": "accessory", "rarity": "common", "emoji": "🌿",
        "description": "Smells weird but the luck is real.",
        "stats": {"luck": 2},
    },
    "wooden_club": {
        "name": "Wooden Club", "slot": "weapon", "rarity": "common", "emoji": "🏏",
        "description": "Bonk. That's the whole strategy.",
        "stats": {"attack": 2, "defense": 1},
    },
    "torn_cape": {
        "name": "Torn Cape", "slot": "armor", "rarity": "common", "emoji": "🧣",
        "description": "Dramatic? Yes. Protective? Barely.",
        "stats": {"defense": 1, "speed": 2},
    },

    # ===== UNCOMMON (Floors 5-25, 30% drop) =====
    "iron_blade": {
        "name": "Iron Blade", "slot": "weapon", "rarity": "uncommon", "emoji": "⚔️",
        "description": "A proper sword. Functional and boring.",
        "stats": {"attack": 7},
    },
    "steel_buckler": {
        "name": "Steel Buckler", "slot": "armor", "rarity": "uncommon", "emoji": "🛡️",
        "description": "Small but effective. Like your brain.",
        "stats": {"defense": 6, "speed": -1},
    },
    "chainmail": {
        "name": "Chainmail", "slot": "armor", "rarity": "uncommon", "emoji": "🔗",
        "description": "Heavy, loud, and effective.",
        "stats": {"defense": 8, "speed": -2},
    },
    "swift_boots": {
        "name": "Swift Boots", "slot": "accessory", "rarity": "uncommon", "emoji": "💨",
        "description": "You can almost outrun your failures.",
        "stats": {"speed": 6, "luck": 1},
    },
    "venom_dagger": {
        "name": "Venom Dagger", "slot": "weapon", "rarity": "uncommon", "emoji": "🗡️",
        "description": "Coated in something nasty.",
        "stats": {"attack": 5, "luck": 3},
    },
    "battle_hammer": {
        "name": "Battle Hammer", "slot": "weapon", "rarity": "uncommon", "emoji": "🔨",
        "description": "Slow but devastating. Like your comebacks.",
        "stats": {"attack": 9, "speed": -1},
    },
    "wolf_pelt": {
        "name": "Wolf Pelt", "slot": "armor", "rarity": "uncommon", "emoji": "🐺",
        "description": "Smells like wet dog. Keeps you alive though.",
        "stats": {"defense": 4, "speed": 3},
    },
    "lucky_coin": {
        "name": "Lucky Coin", "slot": "accessory", "rarity": "uncommon", "emoji": "🪙",
        "description": "Flip it. Heads you win. Tails you also win. Allegedly.",
        "stats": {"luck": 5},
    },

    # ===== RARE (Floors 10-50, 15% drop) =====
    "flamebrand": {
        "name": "Flamebrand", "slot": "weapon", "rarity": "rare", "emoji": "🔥",
        "description": "Burns everything it touches. Including your hand.",
        "stats": {"attack": 12, "speed": 2},
    },
    "guardian_plate": {
        "name": "Guardian Plate", "slot": "armor", "rarity": "rare", "emoji": "🛡️",
        "description": "Forged by someone who actually knew what they were doing.",
        "stats": {"defense": 14, "max_hp": 20},
    },
    "shadow_cloak": {
        "name": "Shadow Cloak", "slot": "armor", "rarity": "rare", "emoji": "🌑",
        "description": "Darkness is his cloak and oldest ally.",
        "stats": {"speed": 10, "luck": 5, "defense": 3},
    },
    "arcane_staff": {
        "name": "Arcane Staff", "slot": "weapon", "rarity": "rare", "emoji": "🪄",
        "description": "Crackles with unstable energy.",
        "stats": {"attack": 10, "luck": 8},
    },
    "berserker_axe": {
        "name": "Berserker Axe", "slot": "weapon", "rarity": "rare", "emoji": "🪓",
        "description": "Hits hard. Makes you dumb. Worth it.",
        "stats": {"attack": 18, "defense": -5},
    },
    "vampiric_blade": {
        "name": "Vampiric Blade", "slot": "weapon", "rarity": "rare", "emoji": "🦇",
        "description": "The blade drinks deep. So do you.",
        "stats": {"attack": 10},
        "lifesteal": 0.03,  # 3% lifesteal
    },
    "frost_shield": {
        "name": "Frost Shield", "slot": "armor", "rarity": "rare", "emoji": "🧊",
        "description": "Cold to the touch. Colder to the face.",
        "stats": {"defense": 12, "speed": 3},
    },
    "thieves_gloves": {
        "name": "Thieves' Gloves", "slot": "accessory", "rarity": "rare", "emoji": "🧤",
        "description": "Five-finger discount on living.",
        "stats": {"speed": 8, "luck": 6},
    },

    # ===== EPIC (Floors 20-80, 5% drop) =====
    "dragonbone_greatsword": {
        "name": "Dragonbone Greatsword", "slot": "weapon", "rarity": "epic", "emoji": "⚔️",
        "description": "Carved from an actual dragon's spine.",
        "stats": {"attack": 25, "speed": -3},
    },
    "aegis_of_light": {
        "name": "Aegis of Light", "slot": "armor", "rarity": "epic", "emoji": "✨",
        "description": "Blinds your enemies. And yourself sometimes.",
        "stats": {"defense": 22, "max_hp": 40, "speed": -2},
    },
    "phantom_veil": {
        "name": "Phantom Veil", "slot": "armor", "rarity": "epic", "emoji": "👻",
        "description": "You phase in and out of reality.",
        "stats": {"speed": 18, "luck": 12, "defense": 8},
    },
    "stormcaller": {
        "name": "Stormcaller", "slot": "weapon", "rarity": "epic", "emoji": "⛈️",
        "description": "Lightning answers your call. Usually.",
        "stats": {"attack": 20, "speed": 8, "luck": 5},
    },
    "blood_gauntlets": {
        "name": "Blood Gauntlets", "slot": "accessory", "rarity": "epic", "emoji": "🩸",
        "description": "Dripping with something uncomfortable.",
        "stats": {"attack": 15, "defense": 5},
        "lifesteal": 0.05,  # 5% lifesteal
    },
    "void_edge": {
        "name": "Void Edge", "slot": "weapon", "rarity": "epic", "emoji": "🌀",
        "description": "Cuts through reality itself. That can't be healthy.",
        "stats": {"attack": 22, "luck": 6},
        "bleed_on_hit": 1,  # Apply 1 bleed stack per hit
    },
    "titans_bulwark": {
        "name": "Titan's Bulwark", "slot": "armor", "rarity": "epic", "emoji": "🏔️",
        "description": "Worn by giants. Feels like carrying a house.",
        "stats": {"defense": 18, "max_hp": 60, "speed": -4},
    },
    "phoenix_feather": {
        "name": "Phoenix Feather", "slot": "accessory", "rarity": "epic", "emoji": "🔥",
        "description": "Warm. Suspiciously warm. Is it... alive?",
        "stats": {"luck": 10, "max_hp": 30},
    },

    # ===== LEGENDARY (Floors 35+, boss drops, 1% chance) =====
    "blackened_sword": {
        "name": "The Blackened Sword", "slot": "weapon", "rarity": "legendary", "emoji": "🗡️",
        "description": "Blade of the Demon King. Don't let the gluttony consume you.",
        "stats": {"attack": 30, "speed": 5},
        "on_kill_stat_chance": 0.5,  # 50% chance to gain 1 random stat on kill
        "on_kill_stat_chance_demon": 1,  # 100% vs demon enemies
    },
    "hourglass_archiver": {
        "name": "Hourglass of the Archiver", "slot": "accessory", "rarity": "legendary", "emoji": "⏳",
        "description": "Magic is, in a way, science.",
        "stats": {"defense": 10, "luck": 15},
        "magic_dodge": 0.30,  # 30% chance to ignore magic attacks
    },
    "spear_red_dragon": {
        "name": "Spear of the Red Dragon", "slot": "weapon", "rarity": "legendary", "emoji": "🔱",
        "description": "You are destined to die. Why not make it interesting?",
        "stats": {"attack": 35},
        "self_damage_pct": 0.05,  # Lose 5% max HP per turn
        "damage_bonus": 0.40,  # +40% damage
    },
    "starforgers_favor": {
        "name": "Starforger's Favor", "slot": "accessory", "rarity": "legendary", "emoji": "⭐",
        "description": "Favors will be paid.",
        "stats": {"luck": 25, "attack": 10},
        "execute_chance": 0.03,  # 3% chance to halve enemy HP per turn
    },
    "dagger_and_pike": {
        "name": "Dagger and Pike", "slot": "weapon", "rarity": "legendary", "emoji": "🗡️",
        "description": "Whispers in the wind... or is it?",
        "stats": {"attack": 22, "speed": 10},
        "bleed_on_hit": 3,  # Apply 3 stacks of bleed (3 dmg/turn each)
        "lifesteal": 0.20,  # Heal 10% of damage dealt
    },
    "banshees_call": {
        "name": "Banshee's Call", "slot": "weapon", "rarity": "legendary", "emoji": "👻",
        "description": "The scream of the dead guides your blade.",
        "stats": {"attack": 18, "speed": 8},
        "crit_chance_bonus": 0.50,  # +50% critical chance
        "crit_damage_bonus": 0.50,  # +50% critical damage multiplier
    },
    "crown_of_the_endless": {
        "name": "Crown of the Endless", "slot": "accessory", "rarity": "legendary", "emoji": "👑",
        "description": "Heavy is the crown.",
        "stats": {"defense": 20, "luck": 15, "max_hp": 50},
        "magic_dodge": 0.15,  # 15% dodge
        "lifesteal": 0.03,  # 3% lifesteal
    },
    # ===== HELMET SLOT =====
    "leather_cap": {
        "name": "Leather Cap", "slot": "helmet", "rarity": "common", "emoji": "🪖",
        "description": "It's a hat. With padding. Barely.",
        "stats": {"defense": 2, "luck": 1},
    },
    "iron_helm": {
        "name": "Iron Helm", "slot": "helmet", "rarity": "uncommon", "emoji": "⛑️",
        "description": "Heavy enough to give you a headache. But you'll live.",
        "stats": {"defense": 5, "max_hp": 10},
    },
    "mage_hood": {
        "name": "Mage Hood", "slot": "helmet", "rarity": "rare", "emoji": "🧙",
        "description": "Enchanted to make you feel smarter. You're not.",
        "stats": {"luck": 7, "defense": 4, "speed": 2},
    },
    "dragon_helm": {
        "name": "Dragon Helm", "slot": "helmet", "rarity": "epic", "emoji": "🐲",
        "description": "Forged from a dragon's skull. Metal as hell.",
        "stats": {"defense": 16, "max_hp": 35, "attack": 5},
    },
    "crown_of_madness": {
        "name": "Crown of Madness", "slot": "helmet", "rarity": "legendary", "emoji": "👹",
        "description": "Madness is a gift. Unwrap it.",
        "stats": {"attack": 20, "luck": 10, "defense": -5},
        "crit_chance_bonus": 0.25,
        "crit_damage_bonus": 0.30,
    },

    # ===== RING SLOT =====
    "copper_ring": {
        "name": "Copper Ring", "slot": "ring", "rarity": "common", "emoji": "💍",
        "description": "Turns your finger green. +1 style.",
        "stats": {"luck": 3},
    },
    "silver_band": {
        "name": "Silver Band", "slot": "ring", "rarity": "uncommon", "emoji": "💍",
        "description": "Simple. Elegant. Slightly enchanted.",
        "stats": {"luck": 4, "speed": 3},
    },
    "ring_of_vitality": {
        "name": "Ring of Vitality", "slot": "ring", "rarity": "rare", "emoji": "💍",
        "description": "Pulses with life force. Literally.",
        "stats": {"max_hp": 25, "defense": 5},
        "lifesteal": 0.02,
    },
    "signet_of_war": {
        "name": "Signet of War", "slot": "ring", "rarity": "epic", "emoji": "💍",
        "description": "Worn by generals. You're not a general. Wear it anyway.",
        "stats": {"attack": 12, "speed": 6, "luck": 4},
        "damage_bonus": 0.10,
    },
    "ring_of_the_void": {
        "name": "Ring of the Void", "slot": "ring", "rarity": "legendary", "emoji": "💍",
        "description": "Stare into the void. It stares back. And flinches.",
        "stats": {"attack": 15, "defense": 15, "luck": 15},
        "magic_dodge": 0.20,
        "execute_chance": 0.02,
    },
}

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


# ========== INVENTORY HELPERS ==========

def give_item(user_id: int, item_id: str) -> tuple[int, bool]:
    """Give an item to a user. Auto-equips if the slot is empty.
    Returns (inventory_row_id, was_auto_equipped)."""
    item = ITEMS[item_id]
    uid = str(user_id)
    slot = item["slot"]

    # Check if anything is equipped in that slot
    db.cursor.execute(
        "SELECT id FROM adventure_inventory WHERE user_id = ? AND slot = ? AND equipped = 1 LIMIT 1",
        (uid, slot)
    )
    slot_empty = db.cursor.fetchone() is None

    db.cursor.execute(
        "INSERT INTO adventure_inventory (user_id, item_id, slot, rarity, stats_json, equipped, quantity) VALUES (?, ?, ?, ?, ?, ?, 1)",
        (uid, item_id, slot, item["rarity"], json.dumps(item["stats"]), 1 if slot_empty else 0)
    )
    db.connection.commit()
    return db.cursor.lastrowid, slot_empty


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
