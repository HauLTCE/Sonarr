"""
memory_store.py - Long-term context storage for Sonarr AI.
"""
import sqlite3
from pathlib import Path
from datetime import datetime, timezone
import logging

logger = logging.getLogger("bot")

DB_PATH = Path("sonarr_memory.db")

class MemoryStore:
    def __init__(self):
        self.connection = None
        self.cursor = None
        self._init_db()
        
    def _init_db(self):
        try:
            self.connection = sqlite3.connect(str(DB_PATH), check_same_thread=False, timeout=10.0)
            self.connection.row_factory = sqlite3.Row
            self.cursor = self.connection.cursor()
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS long_term_memory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    guild_id TEXT NOT NULL,
                    author_id TEXT NOT NULL,
                    author_name TEXT NOT NULL,
                    content TEXT NOT NULL,
                    topic TEXT,
                    timestamp REAL NOT NULL
                )
            ''')
            self.connection.commit()
            logger.info("[MemoryStore] Initialized long_term_memory table.")
        except Exception as e:
            logger.error(f"[MemoryStore] Initialization error: {e}")
            
    def save_memory(self, user_id: str, guild_id: str, author_id: str, author_name: str, content: str, topic: str = "general"):
        """Save a message permanently to the vault."""
        now = datetime.now(timezone.utc).timestamp()
        try:
            self.cursor.execute('''
                INSERT INTO long_term_memory (user_id, guild_id, author_id, author_name, content, topic, timestamp)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            ''', (user_id, guild_id, author_id, author_name, content, topic, now))
            self.connection.commit()
            return True
        except Exception as e:
            logger.error(f"[MemoryStore] Failed to save memory: {e}")
            return False
            
    def get_memories(self, guild_id: str, author_id: str = None, limit: int = 50):
        """Retrieve stored memories for a guild, optionally filtered by the original author."""
        try:
            if author_id:
                self.cursor.execute('''
                    SELECT * FROM long_term_memory 
                    WHERE guild_id = ? AND author_id = ?
                    ORDER BY timestamp DESC LIMIT ?
                ''', (guild_id, author_id, limit))
            else:
                self.cursor.execute('''
                    SELECT * FROM long_term_memory 
                    WHERE guild_id = ?
                    ORDER BY timestamp DESC LIMIT ?
                ''', (guild_id, limit))
            
            rows = self.cursor.fetchall()
            return [dict(row) for row in rows]
        except Exception as e:
            logger.error(f"[MemoryStore] Failed to retrieve memories: {e}")
            return []
            
    def close(self):
        if self.connection:
            self.connection.close()

# Global memory store
memory_store = MemoryStore()
