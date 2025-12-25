import sqlite3
import json
import logging
from pathlib import Path
from datetime import datetime, timezone

logger = logging.getLogger("bot")

DB_PATH = Path("bot_data.db")

class Database:
    """SQLite abstraction layer for economy and AI memory with prepared statements."""
    
    def __init__(self):
        self.connection = None
        self.cursor = None
        self._init_db()
    
    def _init_db(self):
        """Initialize database and create tables if they don't exist."""
        try:
            self.connection = sqlite3.connect(str(DB_PATH), check_same_thread=False)
            self.connection.row_factory = sqlite3.Row
            self.cursor = self.connection.cursor()
            
            # Economy table: user_id, wallet, bank, donations, last_daily
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS economy (
                    user_id TEXT PRIMARY KEY,
                    wallet INTEGER DEFAULT 0,
                    bank INTEGER DEFAULT 0,
                    donations TEXT DEFAULT '{}',
                    last_daily TEXT,
                    updated_at REAL
                )
            ''')
            
            # AI memory table: user_id, timestamp, role, content, archived (0/1)
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS ai_memory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    timestamp REAL NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    archived INTEGER DEFAULT 0
                )
            ''')
            
            # Tier 3.3: Conversation summaries table for memory compression
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS conversation_summaries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    summary_text TEXT NOT NULL,
                    created_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0,
                    date_range_start REAL,
                    date_range_end REAL
                )
            ''')
            
            # Create indexes for faster queries
            self.cursor.execute('''
                CREATE INDEX IF NOT EXISTS idx_user_timestamp 
                ON ai_memory(user_id, timestamp DESC)
            ''')
            
            self.cursor.execute('''
                CREATE INDEX IF NOT EXISTS idx_archived 
                ON ai_memory(archived, user_id)
            ''')
            
            self.cursor.execute('''
                CREATE INDEX IF NOT EXISTS idx_summary_user 
                ON conversation_summaries(user_id, created_at DESC)
            ''')
            
            self.connection.commit()
            logger.info("[Database] SQLite initialized successfully")
        except Exception as e:
            logger.error(f"[Database] Initialization error: {e}")
            raise
    
    # ============== ECONOMY METHODS ==============
    
    def get_user_economy(self, user_id: str):
        """Get economy data for a user."""
        self.cursor.execute(
            'SELECT wallet, bank, donations, last_daily FROM economy WHERE user_id = ?',
            (user_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return {"wallet": 0, "bank": 0, "donations": {}, "last_daily": None}
        
        donations = json.loads(row[2]) if row[2] else {}
        return {
            "wallet": row[0],
            "bank": row[1],
            "donations": donations,
            "last_daily": row[3]
        }
    
    def set_user_economy(self, user_id: str, wallet: int, bank: int, donations: dict, last_daily: str = None):
        """Update economy data for a user."""
        donations_json = json.dumps(donations)
        now = datetime.now(timezone.utc).timestamp()
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO economy 
            (user_id, wallet, bank, donations, last_daily, updated_at)
            VALUES (?, ?, ?, ?, ?, ?)
        ''', (user_id, wallet, bank, donations_json, last_daily, now))
        
        self.connection.commit()
    
    def update_balance(self, user_id: str, wallet_delta: int = 0, bank_delta: int = 0):
        """Update wallet and/or bank by delta amounts (faster than full set)."""
        current = self.get_user_economy(user_id)
        new_wallet = max(0, current["wallet"] + wallet_delta)
        new_bank = max(0, current["bank"] + bank_delta)
        
        self.set_user_economy(user_id, new_wallet, new_bank, current["donations"], current["last_daily"])
    
    def get_all_users_economy(self):
        """Get all user economy data (for startup/backup)."""
        self.cursor.execute('SELECT user_id, wallet, bank, donations, last_daily FROM economy')
        rows = self.cursor.fetchall()
        
        result = {}
        for row in rows:
            donations = json.loads(row[3]) if row[3] else {}
            result[row[0]] = {
                "wallet": row[1],
                "bank": row[2],
                "donations": donations,
                "last_daily": row[4]
            }
        return result
    
    # ============== AI MEMORY METHODS ==============
    
    def add_ai_message(self, user_id: str, role: str, content: str):
        """Add a message to AI memory."""
        timestamp = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('''
            INSERT INTO ai_memory (user_id, timestamp, role, content, archived)
            VALUES (?, ?, ?, ?, 0)
        ''', (user_id, timestamp, role, content))
        
        self.connection.commit()
    
    def get_ai_messages(self, user_id: str, limit: int = 100, include_archived: bool = False):
        """Get recent AI messages for a user (newest first)."""
        if include_archived:
            self.cursor.execute('''
                SELECT role, content FROM ai_memory 
                WHERE user_id = ? 
                ORDER BY timestamp DESC LIMIT ?
            ''', (user_id, limit))
        else:
            self.cursor.execute('''
                SELECT role, content FROM ai_memory 
                WHERE user_id = ? AND archived = 0
                ORDER BY timestamp DESC LIMIT ?
            ''', (user_id, limit))
        
        rows = self.cursor.fetchall()
        # Reverse to get chronological order
        return [{"role": row[0], "content": row[1]} for row in reversed(rows)]
    
    def archive_old_messages(self, user_id: str, days_old: int = 30):
        """Archive messages older than X days for a user."""
        cutoff_time = datetime.now(timezone.utc).timestamp() - (days_old * 86400)
        
        self.cursor.execute('''
            UPDATE ai_memory 
            SET archived = 1 
            WHERE user_id = ? AND timestamp < ? AND archived = 0
        ''', (user_id, cutoff_time))
        
        archived_count = self.cursor.rowcount
        self.connection.commit()
        return archived_count
    
    def get_ai_memory_stats(self, user_id: str):
        """Get stats on AI memory for a user."""
        self.cursor.execute('''
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN archived = 0 THEN 1 ELSE 0 END) as active,
                SUM(CASE WHEN archived = 1 THEN 1 ELSE 0 END) as archived
            FROM ai_memory WHERE user_id = ?
        ''', (user_id,))
        
        row = self.cursor.fetchone()
        return {
            "total": row[0] or 0,
            "active": row[1] or 0,
            "archived": row[2] or 0
        }
    
    def cleanup_archived_messages(self, days_archived: int = 90):
        """Delete messages archived for more than X days."""
        cutoff_time = datetime.now(timezone.utc).timestamp() - (days_archived * 86400)
        
        self.cursor.execute('''
            DELETE FROM ai_memory 
            WHERE archived = 1 AND timestamp < ?
        ''', (cutoff_time,))
        
        deleted_count = self.cursor.rowcount
        self.connection.commit()
        logger.info(f"[Database] Cleaned up {deleted_count} old archived messages")
        return deleted_count
    
    # ============== TIER 3.3: CONVERSATION SUMMARY METHODS ==============
    
    def add_conversation_summary(self, user_id: str, summary_text: str, message_count: int, 
                                date_range_start: float, date_range_end: float):
        """Store a conversation summary."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('''
            INSERT INTO conversation_summaries 
            (user_id, summary_text, created_at, message_count, date_range_start, date_range_end)
            VALUES (?, ?, ?, ?, ?, ?)
        ''', (user_id, summary_text, now, message_count, date_range_start, date_range_end))
        
        self.connection.commit()
        logger.debug(f"[Database] Added summary for user {user_id} ({message_count} messages)")
    
    def get_conversation_summaries(self, user_id: str, limit: int = 10):
        """Get recent conversation summaries for a user."""
        self.cursor.execute('''
            SELECT summary_text, created_at, message_count FROM conversation_summaries
            WHERE user_id = ?
            ORDER BY created_at DESC
            LIMIT ?
        ''', (user_id, limit))
        
        rows = self.cursor.fetchall()
        return [{"text": row[0], "date": row[1], "count": row[2]} for row in rows]
    
    def get_old_messages_for_summary(self, user_id: str, days_old: int = 7):
        """Get all active (non-archived) messages older than X days for summarization."""
        cutoff_time = datetime.now(timezone.utc).timestamp() - (days_old * 86400)
        
        self.cursor.execute('''
            SELECT timestamp, role, content FROM ai_memory
            WHERE user_id = ? AND archived = 0 AND timestamp < ?
            ORDER BY timestamp ASC
        ''', (user_id, cutoff_time))
        
        rows = self.cursor.fetchall()
        return [(row[0], row[1], row[2]) for row in rows]
    
    def archive_messages_before_timestamp(self, user_id: str, timestamp: float):
        """Archive all messages before a specific timestamp (used after summarization)."""
        self.cursor.execute('''
            UPDATE ai_memory
            SET archived = 1
            WHERE user_id = ? AND timestamp < ? AND archived = 0
        ''', (user_id, timestamp))
        
        archived_count = self.cursor.rowcount
        self.connection.commit()
        logger.debug(f"[Database] Archived {archived_count} messages for user {user_id}")
        return archived_count
    
    def close(self):
        """Close database connection."""
        if self.connection:
            self.connection.close()
            logger.info("[Database] Connection closed")

# Global instance
db = Database()
