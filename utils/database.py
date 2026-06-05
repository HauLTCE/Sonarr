import sqlite3
import json
import logging
import os
import asyncio
import threading
from pathlib import Path
from datetime import datetime, timezone
from concurrent.futures import ThreadPoolExecutor

logger = logging.getLogger("bot")

DB_PATH = Path("bot_data.db")

# Check if we should use PostgreSQL
USE_POSTGRES = os.getenv('HA_ENABLED', '').lower() in ('true', '1', 'yes') or os.getenv('DATABASE_URL')

# Thread pool for running async code from sync context
_executor = ThreadPoolExecutor(max_workers=10)


def _run_async_in_thread(coro_func, *args, **kwargs):
    """Run an async function in a separate thread with its own event loop."""
    def run_in_new_loop():
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        try:
            coro = coro_func(*args, **kwargs)
            return loop.run_until_complete(coro)
        finally:
            loop.close()
    
    future = _executor.submit(run_in_new_loop)
    return future.result(timeout=30)

class Database:
    """SQLite abstraction layer for economy and AI memory with prepared statements."""
    
    def __init__(self):
        self.connection = None
        self._local = threading.local()
        self._init_db()

    @property
    def cursor(self):
        """Return a cursor unique to the calling thread.

        The connection is shared (SQLite serializes writes), but a single shared
        cursor is NOT thread-safe: concurrent execute/fetch on one cursor lets
        threads read each other's result rows. Giving each thread its own cursor
        closes that race with zero changes to the 125 `db.cursor.execute(...)`
        call sites. Cursors are cheap and share the connection's transaction.
        """
        cur = getattr(self._local, "cursor", None)
        if cur is None:
            cur = self.connection.cursor()
            self._local.cursor = cur
        return cur

    def _init_db(self):
        """Initialize database and create tables if they don't exist."""
        try:
            self.connection = sqlite3.connect(str(DB_PATH), check_same_thread=False, timeout=10.0)
            self.connection.row_factory = sqlite3.Row

            self.cursor.execute('PRAGMA journal_mode=WAL')
            self.cursor.execute('PRAGMA synchronous=NORMAL')
            self.cursor.execute('PRAGMA cache_size=10000')
            self.connection.commit()
            

            from utils.schema import init_schema
            init_schema(self.cursor)

            self.connection.commit()

            # ========== ONE-TIME MIGRATIONS ==========
            self._migrate_levels_json()
            self._migrate_config_json()

            logger.info("[Database] SQLite initialized successfully")
        except Exception as e:
            logger.error(f"[Database] Initialization error: {e}")
            raise

    def _migrate_levels_json(self):
        """One-time migration from levels.json to DB."""
        levels_file = Path("levels.json")
        if not levels_file.exists():
            return
        try:
            with open(levels_file, "r", encoding="utf-8") as f:
                data = json.load(f)
            if not data:
                return
            count = 0
            for user_id, info in data.items():
                xp = info.get("xp", 0)
                level = info.get("level", 1)
                self.cursor.execute(
                    "INSERT OR IGNORE INTO levels (user_id, guild_id, xp, level) VALUES (?, 'global', ?, ?)",
                    (str(user_id), xp, level)
                )
                count += 1
            self.connection.commit()
            # Rename to prevent re-import
            levels_file.rename("levels.json.migrated")
            logger.info(f"[Database] Migrated {count} users from levels.json to DB")
        except Exception as e:
            logger.warning(f"[Database] levels.json migration failed: {e}")

    def _migrate_config_json(self):
        """One-time migration from server_config.json to DB."""
        config_file = Path("server_config.json")
        if not config_file.exists():
            return
        try:
            with open(config_file, "r", encoding="utf-8") as f:
                data = json.load(f)
            if not data:
                return
            count = 0
            for guild_id, config in data.items():
                for key, value in config.items():
                    self.cursor.execute(
                        "INSERT OR IGNORE INTO guild_config (guild_id, config_key, config_value) VALUES (?, ?, ?)",
                        (str(guild_id), key, str(value))
                    )
                    count += 1
            self.connection.commit()
            # Rename to prevent re-import
            config_file.rename("server_config.json.migrated")
            logger.info(f"[Database] Migrated {count} config entries from server_config.json to DB")
        except Exception as e:
            logger.warning(f"[Database] server_config.json migration failed: {e}")
    
    def get_cached_category(self, msg_hash: str, guild_id: int = None):
        """Get cached category. Returns None if not found or expired (7 days)."""
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 86400)
        
        if guild_id:
            self.cursor.execute(
                'SELECT category, created_at FROM message_cache WHERE msg_hash = ? AND guild_id = ?', 
                (msg_hash, str(guild_id))
            )
        else:
            self.cursor.execute(
                'SELECT category, created_at FROM message_cache WHERE msg_hash = ?', 
                (msg_hash,)
            )
        row = self.cursor.fetchone()
        if row:
            if row[1] < seven_days_ago:
                if guild_id:
                    self.cursor.execute('DELETE FROM message_cache WHERE msg_hash = ? AND guild_id = ?', (msg_hash, str(guild_id)))
                else:
                    self.cursor.execute('DELETE FROM message_cache WHERE msg_hash = ?', (msg_hash,))
                self.connection.commit()
                return None
            
            if guild_id:
                self.cursor.execute(
                    'UPDATE message_cache SET hit_count = hit_count + 1, last_hit = ? WHERE msg_hash = ? AND guild_id = ?',
                    (now, msg_hash, str(guild_id))
                )
            else:
                self.cursor.execute(
                    'UPDATE message_cache SET hit_count = hit_count + 1, last_hit = ? WHERE msg_hash = ?',
                    (now, msg_hash)
                )
            self.connection.commit()
            return row[0]
        return None
    
    def cache_category(self, msg_hash: str, category: str, guild_id = None, content_words: list = None):
        """Cache a message hash → category. Starts at hit_count=1. If full, evict least used."""
        now = datetime.now(timezone.utc).timestamp()
        guild_str = str(guild_id) if guild_id else "global"
        words_json = json.dumps(content_words) if content_words else None
        
        self.cursor.execute('SELECT COUNT(*) FROM message_cache WHERE guild_id = ?', (guild_str,))
        count = self.cursor.fetchone()[0]
        
        if count >= 5000:
            self.cursor.execute(
                'DELETE FROM message_cache WHERE msg_hash = (SELECT msg_hash FROM message_cache WHERE guild_id = ? ORDER BY hit_count ASC, last_hit ASC LIMIT 1)',
                (guild_str,)
            )
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO message_cache (msg_hash, guild_id, category, hit_count, created_at, last_hit, words_json)
            VALUES (?, ?, ?, 1, ?, ?, ?)
        ''', (msg_hash, guild_str, category, now, now, words_json))
        self.connection.commit()
    
    def get_cached_response(self, msg_hash: str):
        """Get cached AI-generated response. Returns None if not found or expired (7 days)."""
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 86400)
        
        self.cursor.execute(
            'SELECT response, created_at FROM response_cache WHERE msg_hash = ?', 
            (msg_hash,)
        )
        row = self.cursor.fetchone()
        if row:
            if row[1] < seven_days_ago:
                self.cursor.execute('DELETE FROM response_cache WHERE msg_hash = ?', (msg_hash,))
                self.connection.commit()
                return None
            
            self.cursor.execute(
                'UPDATE response_cache SET hit_count = hit_count + 1, last_hit = ? WHERE msg_hash = ?',
                (now, msg_hash)
            )
            self.connection.commit()
            return row[0]
        return None
    
    def cache_response(self, msg_hash: str, response: str):
        """Cache an AI-generated response. If full, evict least used."""
        now = datetime.now(timezone.utc).timestamp()
        
        self.cursor.execute('SELECT COUNT(*) FROM response_cache')
        count = self.cursor.fetchone()[0]
        
        if count >= 3000:
            self.cursor.execute('DELETE FROM response_cache WHERE msg_hash = (SELECT msg_hash FROM response_cache ORDER BY hit_count ASC, last_hit ASC LIMIT 1)')
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO response_cache (msg_hash, response, hit_count, created_at, last_hit)
            VALUES (?, ?, 1, ?, ?)
        ''', (msg_hash, response, now, now))
        self.connection.commit()
    
    def fuzzy_search_category(self, content_words: list, min_overlap: float = 0.6):
        """Search for cached entries that share content words. Returns best match or None.
        
        Args:
            content_words: List of content words from the message
            min_overlap: Minimum overlap ratio (0.6 = 60% of words must match)
        """
        if not content_words or len(content_words) < 2:
            return None
        
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 86400)
        
        self.cursor.execute(
            'SELECT category, words_json, hit_count FROM message_cache WHERE created_at > ? AND words_json IS NOT NULL ORDER BY hit_count DESC LIMIT 500',
            (seven_days_ago,)
        )
        rows = self.cursor.fetchall()
        
        if not rows:
            return None
        
        content_set = set(content_words)
        best_match = None
        best_overlap = 0
        
        for row in rows:
            category, words_json, hit_count = row[0], row[1], row[2]
            try:
                cached_words = set(json.loads(words_json))
            except (json.JSONDecodeError, TypeError):
                continue
            
            if not cached_words:
                continue
            
            intersection = len(content_set & cached_words)
            union = len(content_set | cached_words)
            
            if union == 0:
                continue
            
            overlap = intersection / union
            
            score = overlap * (1 + min(hit_count, 10) * 0.05)
            
            if score > best_overlap and overlap >= min_overlap:
                best_overlap = score
                best_match = category
        
        if best_match:
            logger.debug(f"[Cache] Fuzzy match found: {best_match} (score: {best_overlap:.2f})")
        
        return best_match
    
    def get_keep_threshold(self, guild_id: str = "global"):
        """Get per-guild adaptive keep threshold."""
        self.cursor.execute('SELECT threshold FROM guild_cache_config WHERE guild_id = ?', (guild_id,))
        row = self.cursor.fetchone()
        if row:
            return row[0]
        self.cursor.execute('INSERT OR IGNORE INTO guild_cache_config (guild_id, threshold) VALUES (?, ?)', (guild_id, 1))
        self.connection.commit()
        return 1
    
    def set_keep_threshold(self, value: int, guild_id: str = "global"):
        """Set per-guild adaptive keep threshold."""
        value = max(0, value)
        self.cursor.execute('INSERT OR REPLACE INTO guild_cache_config (guild_id, threshold) VALUES (?, ?)', (guild_id, value))
        self.connection.commit()
    
    def cleanup_message_cache(self, guild_id: str = "global"):
        """
        Smart cache cleanup per guild:
        1. Delete entries with hit_count < threshold
        2. Reset remaining hit_counts to 0
        3. Adjust threshold based on remaining count
        """
        threshold = self.get_keep_threshold(guild_id)
        
        self.cursor.execute(
            'DELETE FROM message_cache WHERE guild_id = ? AND hit_count < ?', 
            (guild_id, threshold)
        )
        deleted = self.cursor.rowcount
        
        self.cursor.execute(
            'UPDATE message_cache SET hit_count = 0 WHERE guild_id = ?',
            (guild_id,)
        )
        
        self.cursor.execute('SELECT COUNT(*) FROM message_cache WHERE guild_id = ?', (guild_id,))
        remaining = self.cursor.fetchone()[0]
        
        old_threshold = threshold
        if remaining < 100 and threshold > 0:
            threshold -= 1
            self.set_keep_threshold(threshold, guild_id)
            logger.info(f"[Cache] Guild {guild_id}: Lowered threshold {old_threshold} → {threshold} (only {remaining} remaining)")
        elif remaining > 2000:
            threshold += 1
            self.set_keep_threshold(threshold, guild_id)
            logger.info(f"[Cache] Guild {guild_id}: Raised threshold {old_threshold} → {threshold} ({remaining} remaining)")
        
        self.connection.commit()
        logger.info(f"[Cache] Guild {guild_id}: Cleanup done - deleted {deleted}, remaining {remaining}, threshold {threshold}")
        return deleted, remaining, threshold
    
    def get_cache_stats(self, guild_id: int = None):
        """Get cache statistics for a specific guild or all guilds."""
        if guild_id:
            guild_str = str(guild_id)
            self.cursor.execute(
                'SELECT COUNT(*), SUM(hit_count), AVG(hit_count) FROM message_cache WHERE guild_id = ?',
                (guild_str,)
            )
            row = self.cursor.fetchone()
            return {
                "entries": row[0] or 0,
                "total_hits": row[1] or 0,
                "avg_hits": round(row[2] or 0, 2),
                "threshold": self.get_keep_threshold(guild_str)
            }
        else:
            self.cursor.execute('SELECT COUNT(*), SUM(hit_count), AVG(hit_count) FROM message_cache')
            row = self.cursor.fetchone()
            return {
                "entries": row[0] or 0,
                "total_hits": row[1] or 0,
                "avg_hits": round(row[2] or 0, 2),
                "threshold": "per-guild"
            }
    
    def get_all_cache_guilds(self):
        """Get all unique guild IDs from message cache."""
        self.cursor.execute('SELECT DISTINCT guild_id FROM message_cache')
        return [row[0] for row in self.cursor.fetchall()]
    
    def cleanup_all_guilds_cache(self):
        """Run cleanup for all guilds in the cache."""
        guilds = self.get_all_cache_guilds()
        total_deleted = 0
        total_remaining = 0
        
        for guild_id in guilds:
            deleted, remaining, threshold = self.cleanup_message_cache(guild_id)
            total_deleted += deleted
            total_remaining += remaining
        
        logger.info(f"[Cache] All guilds cleanup: {len(guilds)} guilds, {total_deleted} deleted, {total_remaining} remaining")
        return len(guilds), total_deleted, total_remaining
    
    def close(self):
        """Close database connection."""
        if self.connection:
            self.connection.close()
            logger.info("[Database] Connection closed")

    # ========== MISGENDERING MEMORY METHODS ==========
    
    def record_misgendering(self, user_id: str, guild_id: str, term_used: str):
        """Record when someone misgenders Sonarr."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('''
            INSERT OR REPLACE INTO misgendering_memory (user_id, guild_id, term_used, timestamp)
            VALUES (?, ?, ?, ?)
        ''', (user_id, guild_id, term_used, now))
        self.connection.commit()
    
    def get_misgendered_users(self, guild_id: str, hours_back: int = 24):
        """Get list of users who misgendered Sonarr within the time limit."""
        cutoff = datetime.now(timezone.utc).timestamp() - (hours_back * 3600)
        self.cursor.execute('''
            SELECT user_id, term_used, timestamp FROM misgendering_memory 
            WHERE guild_id = ? AND timestamp > ?
            ORDER BY timestamp DESC
        ''', (guild_id, cutoff))
        rows = self.cursor.fetchall()
        return [{
            "user_id": row[0],
            "term_used": row[1], 
            "timestamp": row[2]
        } for row in rows]
    
    def clear_misgendering_memory(self, user_id: str, guild_id: str):
        """Clear misgendering memory when user corrects themselves."""
        self.cursor.execute('''
            DELETE FROM misgendering_memory WHERE user_id = ? AND guild_id = ?
        ''', (user_id, guild_id))
        self.connection.commit()
    
    def cleanup_expired_misgendering(self, hours_back: int = 24):
        """Remove misgendering memory older than specified hours."""
        cutoff = datetime.now(timezone.utc).timestamp() - (hours_back * 3600)
        self.cursor.execute('''
            DELETE FROM misgendering_memory WHERE timestamp <= ?
        ''', (cutoff,))
        self.connection.commit()



# ============================================================
# Create the appropriate database instance
# ============================================================

def _create_database():
    """Create the database instance.

    NOTE: A PostgreSQL "HA mode" was once scaffolded here but never implemented
    (it imported a `database.postgres` module that does not exist in this repo),
    so it always fell back to SQLite anyway. The dead wrapper was removed. If you
    set HA_ENABLED / DATABASE_URL expecting Postgres, you'll get SQLite + a warning
    until a real backend is built. See docs/refactor-notes/ for the plan.
    """
    if USE_POSTGRES:
        logger.warning(
            "[Database] HA_ENABLED/DATABASE_URL is set, but no PostgreSQL backend "
            "is implemented. Using SQLite."
        )
    logger.info("[Database] Using SQLite backend")
    return Database()


# Global database instance
db = _create_database()