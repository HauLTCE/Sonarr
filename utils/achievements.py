from utils.database import db
import time

def unlock_achievement(user_id: int, achievement_id: str) -> bool:
    uid = str(user_id)
    db.cursor.execute("SELECT 1 FROM achievements WHERE user_id = ? AND achievement_id = ?", (uid, achievement_id))
    if db.cursor.fetchone():
        return False
        
    db.cursor.execute("INSERT INTO achievements (user_id, achievement_id, unlocked_at) VALUES (?, ?, ?)", (uid, achievement_id, time.time()))
    db.connection.commit()
    return True
    
def get_achievements(user_id: int) -> list:
    db.cursor.execute("SELECT achievement_id FROM achievements WHERE user_id = ?", (str(user_id),))
    return [row[0] for row in db.cursor.fetchall()]
