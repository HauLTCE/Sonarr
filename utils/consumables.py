"""Consumable items system — definitions and DB helpers."""
from utils.database import db


# ========== CONSUMABLE DEFINITIONS ==========
CONSUMABLES = {
    "health_potion": {
        "name": "Health Potion", "emoji": "🧪", "cost": 50,
        "desc": "Heal 50 HP instantly.",
        "combat": True,
    },
    "greater_potion": {
        "name": "Greater Potion", "emoji": "🧪", "cost": 150,
        "desc": "Heal to full HP.",
        "combat": True,
    },
    "lucky_charm": {
        "name": "Lucky Charm", "emoji": "🍀", "cost": 120,
        "desc": "+10 LCK for your next 5 combats.",
        "combat": True,
    },
    "revival_token": {
        "name": "Revival Token", "emoji": "💀", "cost": 500,
        "desc": "Prevent floor loss on your next death. (Passive)",
        "combat": False,
    },
    # ===== NEW CONSUMABLES =====
    "strength_tonic": {
        "name": "Strength Tonic", "emoji": "💪", "cost": 80,
        "desc": "+5 ATK for current combat.",
        "combat": True,
    },
    "iron_skin": {
        "name": "Iron Skin", "emoji": "🛡️", "cost": 80,
        "desc": "+5 DEF for current combat.",
        "combat": True,
    },
    "speed_elixir": {
        "name": "Speed Elixir", "emoji": "💨", "cost": 80,
        "desc": "+5 SPD for current combat.",
        "combat": True,
    },
    "antidote": {
        "name": "Antidote", "emoji": "🩹", "cost": 40,
        "desc": "Remove all bleed stacks from you.",
        "combat": True,
    },
    "smoke_bomb": {
        "name": "Smoke Bomb", "emoji": "💣", "cost": 60,
        "desc": "Guaranteed flee from current combat.",
        "combat": True,
    },
    "damage_scroll": {
        "name": "Damage Scroll", "emoji": "📜", "cost": 100,
        "desc": "Deal 50 flat damage to current enemy.",
        "combat": True,
    },
    "shield_scroll": {
        "name": "Shield Scroll", "emoji": "🛡️", "cost": 100,
        "desc": "Block the next enemy attack completely.",
        "combat": True,
    },
    "xp_tome": {
        "name": "XP Tome", "emoji": "📖", "cost": 200,
        "desc": "+50 dungeon XP instantly.",
        "combat": False,
    },
    "floor_skip": {
        "name": "Floor Skip", "emoji": "⏭️", "cost": 300,
        "desc": "Skip current floor, advance +1.",
        "combat": False,
    },
    "warp_crystal": {
        "name": "Warp Crystal", "emoji": "🔮", "cost": 500,
        "desc": "Warp to your deepest floor reached.",
        "combat": False,
    },
}

# Sell price: 30% of cost
SELL_PRICES = {cid: max(1, int(cdata["cost"] * 0.3)) for cid, cdata in CONSUMABLES.items()}


def get_consumables(user_id: int) -> dict:
    """Get all consumables for a user. Returns {item_id: quantity}."""
    uid = str(user_id)
    db.cursor.execute("SELECT item_id, quantity FROM consumable_inventory WHERE user_id = ? AND quantity > 0", (uid,))
    return {row[0]: row[1] for row in db.cursor.fetchall()}


def add_consumable(user_id: int, item_id: str, qty: int = 1):
    """Add consumable(s) to a user's inventory."""
    uid = str(user_id)
    db.cursor.execute(
        "INSERT INTO consumable_inventory (user_id, item_id, quantity) VALUES (?, ?, ?) "
        "ON CONFLICT(user_id, item_id) DO UPDATE SET quantity = quantity + ?",
        (uid, item_id, qty, qty)
    )
    db.connection.commit()


def use_consumable(user_id: int, item_id: str) -> bool:
    """Use one consumable. Returns True if successful, False if none owned."""
    uid = str(user_id)
    db.cursor.execute("SELECT quantity FROM consumable_inventory WHERE user_id = ? AND item_id = ?", (uid, item_id))
    row = db.cursor.fetchone()
    if not row or row[0] <= 0:
        return False
    db.cursor.execute(
        "UPDATE consumable_inventory SET quantity = quantity - 1 WHERE user_id = ? AND item_id = ?",
        (uid, item_id)
    )
    db.connection.commit()
    return True


def remove_consumable(user_id: int, item_id: str, qty: int = 1) -> bool:
    """Remove consumable(s) for selling. Returns True if had enough."""
    uid = str(user_id)
    db.cursor.execute("SELECT quantity FROM consumable_inventory WHERE user_id = ? AND item_id = ?", (uid, item_id))
    row = db.cursor.fetchone()
    if not row or row[0] < qty:
        return False
    db.cursor.execute(
        "UPDATE consumable_inventory SET quantity = quantity - ? WHERE user_id = ? AND item_id = ?",
        (qty, uid, item_id)
    )
    db.connection.commit()
    return True
