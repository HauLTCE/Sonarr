import sqlite3
import json
import logging
import os
import asyncio
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
        self.cursor = None
        self._init_db()
    
    def _init_db(self):
        """Initialize database and create tables if they don't exist."""
        try:
            self.connection = sqlite3.connect(str(DB_PATH), check_same_thread=False, timeout=10.0)
            self.connection.row_factory = sqlite3.Row
            
            self.cursor = self.connection.cursor()
            self.cursor.execute('PRAGMA journal_mode=WAL')
            self.cursor.execute('PRAGMA synchronous=NORMAL')
            self.cursor.execute('PRAGMA cache_size=10000')
            self.connection.commit()
            

            # Economy table
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS economy (
                    user_id TEXT PRIMARY KEY,
                    wallet INTEGER DEFAULT 0,
                    bank INTEGER DEFAULT 0,
                    bank_cap INTEGER DEFAULT 5000,
                    gems INTEGER DEFAULT 0,
                    total_earned INTEGER DEFAULT 0,
                    total_lost INTEGER DEFAULT 0,
                    total_gambled INTEGER DEFAULT 0,
                    daily_streak INTEGER DEFAULT 0,
                    last_daily REAL DEFAULT 0,
                    last_work REAL DEFAULT 0,
                    last_rob REAL DEFAULT 0,
                    last_cashout REAL DEFAULT 0,
                    times_robbed INTEGER DEFAULT 0,
                    active_title TEXT DEFAULT NULL,
                    prestige INTEGER DEFAULT 0,
                    created_at REAL NOT NULL
                )
            ''')
            self.connection.commit()

            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS message_cache (
                    msg_hash TEXT PRIMARY KEY,
                    guild_id TEXT DEFAULT 'global',
                    category TEXT NOT NULL,
                    hit_count INTEGER DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_hit REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS response_cache (
                    msg_hash TEXT PRIMARY KEY,
                    response TEXT NOT NULL,
                    hit_count INTEGER DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_hit REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS cache_config (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS guild_cache_config (
                    guild_id TEXT PRIMARY KEY,
                    threshold INTEGER DEFAULT 1
                )
            ''')
            
            self.cursor.execute('INSERT OR IGNORE INTO cache_config (key, value) VALUES (?, ?)', ('keep_threshold', 10))
            self.cursor.execute('CREATE INDEX IF NOT EXISTS idx_cache_hits ON message_cache(hit_count DESC)')
            self.cursor.execute('CREATE INDEX IF NOT EXISTS idx_cache_last_hit ON message_cache(last_hit ASC)')
            
            try:
                self.cursor.execute('SELECT guild_id FROM message_cache LIMIT 1')
            except sqlite3.OperationalError:
                logger.info("[Database] Migrating message_cache: adding guild_id column")
                self.cursor.execute('ALTER TABLE message_cache ADD COLUMN guild_id TEXT DEFAULT "global"')
            
            # Add words_json column for fuzzy search (preserves existing data)
            self.cursor.execute("PRAGMA table_info(message_cache)")
            cache_columns = {row[1] for row in self.cursor.fetchall()}
            if "words_json" not in cache_columns:
                logger.info("[Database] Migrating message_cache: adding words_json column for fuzzy search")
                self.cursor.execute('ALTER TABLE message_cache ADD COLUMN words_json TEXT')
            
            # ========== MISGENDERING MEMORY SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS misgendering_memory (
                    user_id TEXT NOT NULL,
                    guild_id TEXT NOT NULL,
                    term_used TEXT NOT NULL,
                    timestamp REAL NOT NULL,
                    PRIMARY KEY (user_id, guild_id)
                )
            ''')
            
            # ========== PROGRESSION SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS achievements (
                    user_id TEXT NOT NULL,
                    achievement_id TEXT NOT NULL,
                    unlocked_at REAL NOT NULL,
                    PRIMARY KEY (user_id, achievement_id)
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS gambling_stats (
                    user_id TEXT NOT NULL,
                    game_type TEXT NOT NULL,
                    games_played INTEGER DEFAULT 0,
                    games_won INTEGER DEFAULT 0,
                    total_wagered INTEGER DEFAULT 0,
                    total_won INTEGER DEFAULT 0,
                    total_lost INTEGER DEFAULT 0,
                    biggest_win INTEGER DEFAULT 0,
                    biggest_loss INTEGER DEFAULT 0,
                    current_streak INTEGER DEFAULT 0,
                    best_streak INTEGER DEFAULT 0,
                    PRIMARY KEY (user_id, game_type)
                )
            ''')

            # ========== ADVENTURE SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS adventure_character (
                    user_id TEXT PRIMARY KEY,
                    current_floor INTEGER DEFAULT 1,
                    deepest_floor INTEGER DEFAULT 1,
                    hp INTEGER DEFAULT 100,
                    max_hp INTEGER DEFAULT 100,
                    base_attack INTEGER DEFAULT 10,
                    base_defense INTEGER DEFAULT 5,
                    base_speed INTEGER DEFAULT 10,
                    base_luck INTEGER DEFAULT 5,
                    bosses_killed INTEGER DEFAULT 0,
                    total_deaths INTEGER DEFAULT 0,
                    skill_cooldown INTEGER DEFAULT 0,
                    dungeon_level INTEGER DEFAULT 1,
                    dungeon_xp INTEGER DEFAULT 0
                )
            ''')

            # Migration: add dungeon_level/xp columns if missing
            self.cursor.execute("PRAGMA table_info(adventure_character)")
            adv_columns = {row[1] for row in self.cursor.fetchall()}
            if "dungeon_level" not in adv_columns:
                logger.info("[Database] Migrating adventure_character: adding dungeon_level column")
                self.cursor.execute('ALTER TABLE adventure_character ADD COLUMN dungeon_level INTEGER DEFAULT 1')
            if "dungeon_xp" not in adv_columns:
                logger.info("[Database] Migrating adventure_character: adding dungeon_xp column")
                self.cursor.execute('ALTER TABLE adventure_character ADD COLUMN dungeon_xp INTEGER DEFAULT 0')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS adventure_inventory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    slot TEXT,
                    rarity TEXT DEFAULT 'common',
                    stats_json TEXT DEFAULT '{}',
                    equipped INTEGER DEFAULT 0,
                    quantity INTEGER DEFAULT 1
                )
            ''')
            # ========== CONSUMABLE SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS consumable_inventory (
                    user_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    quantity INTEGER DEFAULT 0,
                    PRIMARY KEY (user_id, item_id)
                )
            ''')


            # ========== LEVELS SYSTEM (migrated from JSON) ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS levels (
                    user_id TEXT NOT NULL,
                    guild_id TEXT NOT NULL DEFAULT 'global',
                    xp INTEGER DEFAULT 0,
                    level INTEGER DEFAULT 1,
                    PRIMARY KEY (user_id, guild_id)
                )
            ''')

            # ========== GUILD CONFIG (migrated from JSON) ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS guild_config (
                    guild_id TEXT NOT NULL,
                    config_key TEXT NOT NULL,
                    config_value TEXT,
                    PRIMARY KEY (guild_id, config_key)
                )
            ''')

            # ========== TITLE ROLES ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS title_roles (
                    guild_id TEXT NOT NULL,
                    role_name TEXT NOT NULL,
                    role_id TEXT NOT NULL,
                    category TEXT DEFAULT 'economy',
                    color INTEGER DEFAULT 0,
                    PRIMARY KEY (guild_id, role_name)
                )
            ''')

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
# PostgreSQL Wrapper for HA Mode
# ============================================================

class PostgresWrapper:
    """
    Synchronous wrapper around the async PostgresDatabase.
    Allows existing sync code to work with PostgreSQL.
    """
    
    def __init__(self, database_url: str):
        self.database_url = database_url
        self._pg_db = None
        self._pg_loop = None
        self._initialized = False
        self._sqlite_fallback = None
        self._init_error = None
        self._init_lock = False
        
        # Try to initialize immediately (in a separate thread)
        self._try_init()
    
    def _try_init(self):
        """Try to initialize PostgreSQL connection."""
        if self._initialized or self._init_error or self._init_lock:
            return
        
        self._init_lock = True
        
        try:
            # Initialize in a separate thread with its own event loop
            def init_pg():
                from database.postgres import PostgresDatabase
                
                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                
                pg_db = PostgresDatabase(self.database_url)
                loop.run_until_complete(pg_db.connect())
                
                return pg_db, loop
            
            self._pg_db, self._pg_loop = _executor.submit(init_pg).result(timeout=30)
            self._initialized = True
            logger.info("[Database] PostgreSQL connection established (HA mode)")
        except Exception as e:
            self._init_error = str(e)
            logger.error(f"[Database] PostgreSQL init failed: {e}, falling back to SQLite")
            self._sqlite_fallback = Database()
        finally:
            self._init_lock = False
    
    def _run_sync(self, async_method, *args, **kwargs):
        """Run async method synchronously using thread pool."""
        if not self._initialized:
            self._try_init()
        
        if self._pg_db is None:
            raise RuntimeError("PostgreSQL not initialized, use fallback")
        
        def run_in_pg_loop():
            return self._pg_loop.run_until_complete(async_method(*args, **kwargs))
        
        return _executor.submit(run_in_pg_loop).result(timeout=30)
    
    def _get_fallback(self):
        """Get or create SQLite fallback."""
        if self._sqlite_fallback is None:
            self._sqlite_fallback = Database()
        return self._sqlite_fallback
    
    # ========== ECONOMY METHODS ==========
    
    def get_cached_category(self, msg_hash: str, guild_id: int = None) -> str | None:
        if self._pg_db:
            return self._run_sync(self._pg_db.get_cached_category, msg_hash, guild_id)
        return self._get_fallback().get_cached_category(msg_hash, guild_id)
    
    def cache_category(self, msg_hash: str, category: str, guild_id=None, content_words: list = None):
        if self._pg_db:
            return self._run_sync(self._pg_db.cache_category, msg_hash, category, guild_id, content_words)
        return self._get_fallback().cache_category(msg_hash, category, guild_id, content_words)
    
    # ========== MISGENDERING MEMORY ==========
    
    def record_misgendering(self, user_id: str, guild_id: str, term_used: str):
        if self._pg_db:
            return self._run_sync(self._pg_db.record_misgendering, str(user_id), str(guild_id), term_used)
        return self._get_fallback().record_misgendering(str(user_id), str(guild_id), term_used)
    
    def get_misgendered_users(self, guild_id: str, hours_back: int = 24) -> list:
        if self._pg_db:
            return self._run_sync(self._pg_db.get_misgendered_users, str(guild_id), hours_back)
        return self._get_fallback().get_misgendered_users(str(guild_id), hours_back)
    
    def clear_misgendering_memory(self, user_id: str, guild_id: str):
        if self._pg_db:
            return self._run_sync(self._pg_db.clear_misgendering_memory, str(user_id), str(guild_id))
        return self._get_fallback().clear_misgendering_memory(str(user_id), str(guild_id))
    
    def cleanup_expired_misgendering(self, hours_back: int = 24):
        if self._pg_db:
            return self._run_sync(self._pg_db.cleanup_expired_misgendering, hours_back)
        return self._get_fallback().cleanup_expired_misgendering(hours_back)
    
    # ========== FALLBACK TO SQLITE FOR UNIMPLEMENTED METHODS ==========
    
    def __getattr__(self, name):
        """
        For methods not yet implemented in PostgresWrapper,
        fall back to a local SQLite database.
        """
        # Prevent recursion - check for internal attributes first
        if name.startswith('_'):
            raise AttributeError(f"'{type(self).__name__}' object has no attribute '{name}'")
        
        # Initialize fallback if needed
        if '_sqlite_fallback' not in self.__dict__:
            object.__setattr__(self, '_sqlite_fallback', Database())
            logger.warning(f"[Database] PostgreSQL fallback to SQLite initialized")
        
        attr = getattr(self._sqlite_fallback, name, None)
        if attr is None:
            raise AttributeError(f"'{type(self).__name__}' object has no attribute '{name}'")
        return attr


# ============================================================
# Create the appropriate database instance
# ============================================================

def _create_database():
    """Create the appropriate database based on environment."""
    if USE_POSTGRES:
        database_url = os.getenv('DATABASE_URL')
        if database_url:
            logger.info("[Database] HA mode enabled - using PostgreSQL")
            return PostgresWrapper(database_url)
        else:
            logger.warning("[Database] HA_ENABLED but no DATABASE_URL - falling back to SQLite")
    
    logger.info("[Database] Using SQLite backend")
    return Database()


# Global database instance
db = _create_database()