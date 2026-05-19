"""Consumable items system — definitions loaded from JSON, DB helpers."""
import json
import os
from utils.database import db


# ========== LOAD CONSUMABLE DATA FROM JSON ==========
_CONSUMABLES_PATH = os.path.join(os.path.dirname(__file__), "consumables_data.json")
with open(_CONSUMABLES_PATH, "r", encoding="utf-8") as _f:
    CONSUMABLES = json.load(_f)

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
