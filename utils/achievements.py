from utils.database import db
import time

def unlock_achievement(user_id: int, achievement_id: str) -> bool:
    """Unlock an achievement. Returns True only if it was newly unlocked.

    INSERT OR IGNORE on the (user_id, achievement_id) PK is atomic; rowcount tells
    us whether this call actually inserted (newly unlocked) vs. it already existed.
    The old SELECT-then-INSERT could double-insert / raise on a same-user race.
    """
    uid = str(user_id)
    db.cursor.execute(
        "INSERT OR IGNORE INTO achievements (user_id, achievement_id, unlocked_at) VALUES (?, ?, ?)",
        (uid, achievement_id, time.time())
    )
    db.connection.commit()
    return db.cursor.rowcount > 0
    
def get_achievements(user_id: int) -> list:
    db.cursor.execute("SELECT achievement_id FROM achievements WHERE user_id = ?", (str(user_id),))
    return [row[0] for row in db.cursor.fetchall()]
