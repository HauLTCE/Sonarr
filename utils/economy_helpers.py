import math
import time
from utils.database import db

def ensure_account(user_id: int) -> None:
    """Creates economy row if it doesn't exist."""
    user_str = str(user_id)
    # Check if exists
    db.cursor.execute("SELECT 1 FROM economy WHERE user_id = ?", (user_str,))
    if not db.cursor.fetchone():
        now = time.time()
        db.cursor.execute(
            "INSERT INTO economy (user_id, created_at) VALUES (?, ?)", 
            (user_str, now)
        )
        db.connection.commit()

def get_balance(user_id: int) -> dict:
    """Returns {'wallet': int, 'bank': int, 'gems': int, 'bank_cap': int}"""
    ensure_account(user_id)
    db.cursor.execute(
        "SELECT wallet, bank, gems, bank_cap FROM economy WHERE user_id = ?", 
        (str(user_id),)
    )
    row = db.cursor.fetchone()
    return {
        "wallet": row[0],
        "bank": row[1],
        "gems": row[2],
        "bank_cap": row[3]
    }

def update_wallet(user_id: int, amount: int) -> int:
    """Add/subtract from wallet. Returns new balance. Clamps at 0."""
    ensure_account(user_id)
    user_str = str(user_id)
    
    if amount > 0:
        db.cursor.execute(
            "UPDATE economy SET wallet = wallet + ?, total_earned = total_earned + ? WHERE user_id = ?",
            (amount, amount, user_str)
        )
    elif amount < 0:
        # Clamp: don't go below 0
        db.cursor.execute(
            "UPDATE economy SET wallet = MAX(0, wallet + ?) WHERE user_id = ?",
            (amount, user_str)
        )
    db.connection.commit()
    
    # Return new balance
    db.cursor.execute("SELECT wallet FROM economy WHERE user_id = ?", (user_str,))
    row = db.cursor.fetchone()
    return row[0] if row else 0

def spend_wallet(user_id: int, amount: int) -> bool:
    """Atomically deduct `amount` only if wallet >= amount.

    Returns True if the deduction happened, False if the wallet was insufficient.
    Unlike update_wallet (which clamps at 0 and always "succeeds"), this is the
    safe primitive for purchases: the WHERE guard + rowcount check makes the
    read-and-deduct a single atomic step, closing the TOCTOU race where a
    concurrent spend could let a user buy something they can't afford.
    """
    if amount <= 0:
        return True
    ensure_account(user_id)
    db.cursor.execute(
        "UPDATE economy SET wallet = wallet - ? WHERE user_id = ? AND wallet >= ?",
        (amount, str(user_id), amount),
    )
    db.connection.commit()
    return db.cursor.rowcount > 0


def spend_gems(user_id: int, amount: int) -> bool:
    """Atomically deduct gems only if gems >= amount. Returns True if spent.

    Gems have no MAX(0, ...) clamp anywhere, so a plain UPDATE could drive them
    negative — this guard prevents that.
    """
    if amount <= 0:
        return True
    ensure_account(user_id)
    db.cursor.execute(
        "UPDATE economy SET gems = gems - ? WHERE user_id = ? AND gems >= ?",
        (amount, str(user_id), amount),
    )
    db.connection.commit()
    return db.cursor.rowcount > 0


def update_bank(user_id: int, amount: int) -> int:
    """Add/subtract from bank. Respects bank_cap. Returns new balance."""
    ensure_account(user_id)
    bal = get_balance(user_id)
    new_bank = max(0, min(bal["bank_cap"], bal["bank"] + amount))
    
    db.cursor.execute(
        "UPDATE economy SET bank = ? WHERE user_id = ?",
        (new_bank, str(user_id))
    )
    db.connection.commit()
    return new_bank

def transfer_coins(from_id: int, to_id: int, amount: int) -> bool:
    """Atomic transfer between two users. Returns success.

    Deducts from the sender first via the conditional spend_wallet; only credits
    the recipient if that deduction actually happened, so a failure can never
    create or destroy coins.
    """
    if amount <= 0:
        return False

    ensure_account(from_id)
    ensure_account(to_id)

    if not spend_wallet(from_id, amount):
        return False
    update_wallet(to_id, amount)
    return True

def get_leaderboard(limit: int = 10) -> list:
    """Returns top users by wallet + bank combined."""
    db.cursor.execute(
        "SELECT user_id, wallet, bank, (wallet + bank) as total FROM economy ORDER BY total DESC LIMIT ?",
        (limit,)
    )
    return [{"user_id": row[0], "wallet": row[1], "bank": row[2], "total": row[3]} for row in db.cursor.fetchall()]

def record_gamble(user_id: int, wagered: int, net_result: int) -> None:
    """Track gambling stats. net_result is positive for wins, negative for losses."""
    ensure_account(user_id)
    user_str = str(user_id)
    
    db.cursor.execute(
        "UPDATE economy SET total_gambled = total_gambled + ? WHERE user_id = ?",
        (wagered, user_str)
    )
    if net_result < 0:
        db.cursor.execute(
            "UPDATE economy SET total_lost = total_lost + ? WHERE user_id = ?",
            (abs(net_result), user_str)
        )
    db.connection.commit()

def calculate_cashout(levels: int) -> int:
    """
    Coin payout for cashing out X levels.
    """
    if levels <= 0:
        return 0
    
    total = 0
    for i in range(1, levels + 1):
        level_value = 3 + (2.5 * math.log(i + 1))
        total += int(level_value)
    
    return total
