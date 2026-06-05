"""SQLite schema for the bot — all CREATE TABLE / INDEX / ALTER DDL.

Extracted from database.py so the Database class is just the access layer.
init_schema(cursor) is idempotent: every statement is IF NOT EXISTS or a
guarded migration, safe to run on every boot.
"""
import logging
import sqlite3

logger = logging.getLogger("bot")


def init_schema(cursor):
    """Create all tables/indexes and run guarded column migrations."""
    # Economy table
    cursor.execute('''
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

    cursor.execute('''
        CREATE TABLE IF NOT EXISTS message_cache (
            msg_hash TEXT PRIMARY KEY,
            guild_id TEXT DEFAULT 'global',
            category TEXT NOT NULL,
            hit_count INTEGER DEFAULT 1,
            created_at REAL NOT NULL,
            last_hit REAL NOT NULL
        )
    ''')
    
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS response_cache (
            msg_hash TEXT PRIMARY KEY,
            response TEXT NOT NULL,
            hit_count INTEGER DEFAULT 1,
            created_at REAL NOT NULL,
            last_hit REAL NOT NULL
        )
    ''')
    
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS cache_config (
            key TEXT PRIMARY KEY,
            value INTEGER NOT NULL
        )
    ''')
    
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS guild_cache_config (
            guild_id TEXT PRIMARY KEY,
            threshold INTEGER DEFAULT 1
        )
    ''')
    
    cursor.execute('INSERT OR IGNORE INTO cache_config (key, value) VALUES (?, ?)', ('keep_threshold', 10))
    cursor.execute('CREATE INDEX IF NOT EXISTS idx_cache_hits ON message_cache(hit_count DESC)')
    cursor.execute('CREATE INDEX IF NOT EXISTS idx_cache_last_hit ON message_cache(last_hit ASC)')
    
    try:
        cursor.execute('SELECT guild_id FROM message_cache LIMIT 1')
    except sqlite3.OperationalError:
        logger.info("[Database] Migrating message_cache: adding guild_id column")
        cursor.execute('ALTER TABLE message_cache ADD COLUMN guild_id TEXT DEFAULT "global"')
    
    # Add words_json column for fuzzy search (preserves existing data)
    cursor.execute("PRAGMA table_info(message_cache)")
    cache_columns = {row[1] for row in cursor.fetchall()}
    if "words_json" not in cache_columns:
        logger.info("[Database] Migrating message_cache: adding words_json column for fuzzy search")
        cursor.execute('ALTER TABLE message_cache ADD COLUMN words_json TEXT')
    
    # ========== MISGENDERING MEMORY SYSTEM ==========
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS misgendering_memory (
            user_id TEXT NOT NULL,
            guild_id TEXT NOT NULL,
            term_used TEXT NOT NULL,
            timestamp REAL NOT NULL,
            PRIMARY KEY (user_id, guild_id)
        )
    ''')
    
    # ========== PROGRESSION SYSTEM ==========
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS achievements (
            user_id TEXT NOT NULL,
            achievement_id TEXT NOT NULL,
            unlocked_at REAL NOT NULL,
            PRIMARY KEY (user_id, achievement_id)
        )
    ''')
    
    cursor.execute('''
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
    cursor.execute('''
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
    cursor.execute("PRAGMA table_info(adventure_character)")
    adv_columns = {row[1] for row in cursor.fetchall()}
    if "dungeon_level" not in adv_columns:
        logger.info("[Database] Migrating adventure_character: adding dungeon_level column")
        cursor.execute('ALTER TABLE adventure_character ADD COLUMN dungeon_level INTEGER DEFAULT 1')
    if "dungeon_xp" not in adv_columns:
        logger.info("[Database] Migrating adventure_character: adding dungeon_xp column")
        cursor.execute('ALTER TABLE adventure_character ADD COLUMN dungeon_xp INTEGER DEFAULT 0')
    
    cursor.execute('''
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
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS consumable_inventory (
            user_id TEXT NOT NULL,
            item_id TEXT NOT NULL,
            quantity INTEGER DEFAULT 0,
            PRIMARY KEY (user_id, item_id)
        )
    ''')


    # ========== LEVELS SYSTEM (migrated from JSON) ==========
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS levels (
            user_id TEXT NOT NULL,
            guild_id TEXT NOT NULL DEFAULT 'global',
            xp INTEGER DEFAULT 0,
            level INTEGER DEFAULT 1,
            PRIMARY KEY (user_id, guild_id)
        )
    ''')

    # ========== GUILD CONFIG (migrated from JSON) ==========
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS guild_config (
            guild_id TEXT NOT NULL,
            config_key TEXT NOT NULL,
            config_value TEXT,
            PRIMARY KEY (guild_id, config_key)
        )
    ''')

    # ========== TITLE ROLES ==========
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS title_roles (
            guild_id TEXT NOT NULL,
            role_name TEXT NOT NULL,
            role_id TEXT NOT NULL,
            category TEXT DEFAULT 'economy',
            color INTEGER DEFAULT 0,
            PRIMARY KEY (guild_id, role_name)
        )
    ''')
