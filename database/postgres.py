"""
PostgreSQL Database Backend for Sonarr Bot

This module provides async PostgreSQL support for distributed deployments.
Uses asyncpg for high-performance async database operations.
"""

import asyncio
import json
import logging
from datetime import datetime, timezone
from typing import Optional, Any, Dict, List

try:
    import asyncpg
    HAS_ASYNCPG = True
except ImportError:
    HAS_ASYNCPG = False
    asyncpg = None

logger = logging.getLogger("bot.db")


class PostgresDatabase:
    """
    PostgreSQL database backend with async support.
    
    Compatible API with the SQLite Database class but uses
    asyncpg for PostgreSQL connections.
    """
    
    def __init__(self, database_url: str = None):
        self.database_url = database_url
        self.pool: Optional[asyncpg.Pool] = None
        self._initialized = False
    
    async def connect(self):
        """Establish connection pool to PostgreSQL."""
        if not HAS_ASYNCPG:
            raise ImportError("asyncpg is required for PostgreSQL support. Install with: pip install asyncpg")
        
        if self.pool:
            return
        
        try:
            self.pool = await asyncpg.create_pool(
                self.database_url,
                min_size=2,
                max_size=10,
                command_timeout=60
            )
            logger.info("[Database] PostgreSQL connection pool created")
            
            await self._init_tables()
            self._initialized = True
            
        except Exception as e:
            logger.error(f"[Database] PostgreSQL connection failed: {e}")
            raise
    
    async def close(self):
        """Close the connection pool."""
        if self.pool:
            await self.pool.close()
            self.pool = None
            logger.info("[Database] PostgreSQL connection pool closed")
    
    async def _init_tables(self):
        """Initialize all database tables."""
        async with self.pool.acquire() as conn:
            # ========== ECONOMY SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS economy (
                    user_id TEXT PRIMARY KEY,
                    wallet BIGINT DEFAULT 0,
                    bank BIGINT DEFAULT 0,
                    donations JSONB DEFAULT '{}',
                    last_daily TEXT,
                    daily_streak INTEGER DEFAULT 0,
                    updated_at REAL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS inventory (
                    user_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    quantity INTEGER DEFAULT 0,
                    updated_at REAL,
                    PRIMARY KEY (user_id, item_id)
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS active_buffs (
                    user_id TEXT NOT NULL,
                    buff_id TEXT NOT NULL,
                    value REAL DEFAULT 0,
                    expires_at REAL NOT NULL,
                    PRIMARY KEY (user_id, buff_id)
                )
            ''')
            
            # ========== POKEMON SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_owned (
                    pokemon_id TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    species_id TEXT NOT NULL,
                    nickname TEXT,
                    rarity TEXT NOT NULL,
                    level INTEGER DEFAULT 1,
                    xp INTEGER DEFAULT 0,
                    ivs_json JSONB DEFAULT '{}',
                    trait TEXT,
                    current_hp INTEGER DEFAULT 0,
                    is_fainted INTEGER DEFAULT 0,
                    is_equipped INTEGER DEFAULT 0,
                    created_at REAL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_encounters (
                    owner_id TEXT PRIMARY KEY,
                    species_id TEXT NOT NULL,
                    rarity TEXT NOT NULL,
                    level INTEGER NOT NULL,
                    current_hp INTEGER DEFAULT 0,
                    max_hp INTEGER DEFAULT 0,
                    zone_id TEXT,
                    expires_at REAL NOT NULL,
                    attempts INTEGER DEFAULT 0
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_daily (
                    owner_id TEXT PRIMARY KEY,
                    last_claim TEXT
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_pokedex (
                    owner_id TEXT NOT NULL,
                    species_id TEXT NOT NULL,
                    caught INTEGER DEFAULT 0,
                    first_caught REAL,
                    PRIMARY KEY (owner_id, species_id)
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_hunt_cooldowns (
                    owner_id TEXT NOT NULL,
                    zone_id TEXT NOT NULL,
                    next_available_at REAL,
                    PRIMARY KEY (owner_id, zone_id)
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_daycare (
                    pokemon_id TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    start_at REAL,
                    end_at REAL,
                    xp_per_hour INTEGER,
                    cost INTEGER
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_weekly (
                    owner_id TEXT PRIMARY KEY,
                    week_start TEXT,
                    hunts INTEGER DEFAULT 0,
                    catches INTEGER DEFAULT 0,
                    levels INTEGER DEFAULT 0,
                    reward_claimed INTEGER DEFAULT 0
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_capacity (
                    owner_id TEXT PRIMARY KEY,
                    extra_slots INTEGER DEFAULT 0
                )
            ''')
            
            # ========== CACHE SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS message_cache (
                    msg_hash TEXT PRIMARY KEY,
                    guild_id TEXT DEFAULT 'global',
                    category TEXT NOT NULL,
                    words_json TEXT,
                    hit_count INTEGER DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_hit REAL NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS response_cache (
                    msg_hash TEXT PRIMARY KEY,
                    response TEXT NOT NULL,
                    hit_count INTEGER DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_hit REAL NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS cache_config (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS guild_cache_config (
                    guild_id TEXT PRIMARY KEY,
                    threshold INTEGER DEFAULT 1
                )
            ''')
            
            # ========== LOAN SHARK SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS loans (
                    user_id TEXT PRIMARY KEY,
                    principal BIGINT NOT NULL,
                    interest_rate REAL NOT NULL,
                    amount_owed BIGINT NOT NULL,
                    deadline_timestamp REAL NOT NULL,
                    created_timestamp REAL NOT NULL,
                    status TEXT DEFAULT 'active',
                    collateral_pokemon_id TEXT,
                    last_interest_applied REAL,
                    late_notice_count INTEGER DEFAULT 0
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS collateral_locker (
                    user_id TEXT NOT NULL,
                    pokemon_id TEXT NOT NULL,
                    repo_timestamp REAL NOT NULL,
                    buyback_cost BIGINT NOT NULL,
                    original_loan_amount BIGINT NOT NULL,
                    PRIMARY KEY (user_id, pokemon_id)
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS bankruptcies (
                    user_id TEXT PRIMARY KEY,
                    timestamp REAL NOT NULL,
                    shame_role_expires REAL NOT NULL,
                    total_debt_forgiven BIGINT DEFAULT 0,
                    can_borrow_after REAL NOT NULL
                )
            ''')
            
            # ========== STOCK MARKET SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS stocks (
                    ticker TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    price BIGINT NOT NULL,
                    previous_price BIGINT NOT NULL,
                    volatility TEXT DEFAULT 'medium',
                    last_updated REAL NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS stock_portfolio (
                    user_id TEXT NOT NULL,
                    ticker TEXT NOT NULL,
                    shares BIGINT NOT NULL,
                    avg_buy_price BIGINT NOT NULL,
                    PRIMARY KEY (user_id, ticker)
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS stock_history (
                    id SERIAL PRIMARY KEY,
                    ticker TEXT NOT NULL,
                    price BIGINT NOT NULL,
                    timestamp REAL NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS market_news (
                    id SERIAL PRIMARY KEY,
                    headline TEXT NOT NULL,
                    affected_ticker TEXT,
                    effect TEXT,
                    timestamp REAL NOT NULL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS work_activity (
                    date TEXT PRIMARY KEY,
                    work_count INTEGER DEFAULT 0
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS market_channels (
                    guild_id TEXT PRIMARY KEY,
                    channel_id BIGINT NOT NULL
                )
            ''')
            
            # ========== MISGENDERING MEMORY SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS misgendering_memory (
                    user_id TEXT NOT NULL,
                    guild_id TEXT NOT NULL,
                    term_used TEXT NOT NULL,
                    timestamp REAL NOT NULL,
                    PRIMARY KEY (user_id, guild_id)
                )
            ''')
            
            # ========== LEVELING SYSTEM ==========
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS levels (
                    user_id TEXT PRIMARY KEY,
                    xp INTEGER DEFAULT 0,
                    level INTEGER DEFAULT 1,
                    last_message_time REAL
                )
            ''')
            
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS command_history (
                    id SERIAL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    command TEXT NOT NULL,
                    timestamp REAL NOT NULL
                )
            ''')
            
            # ========== INDEXES ==========
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_cache_hits ON message_cache(hit_count DESC)')
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_cache_last_hit ON message_cache(last_hit ASC)')
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_loans_status ON loans(status)')
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_loans_deadline ON loans(deadline_timestamp)')
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_stock_history_ticker ON stock_history(ticker, timestamp DESC)')
            await conn.execute('CREATE INDEX IF NOT EXISTS idx_pokemon_owner ON pokemon_owned(owner_id)')
            
            # Insert default config
            await conn.execute('''
                INSERT INTO cache_config (key, value) VALUES ('keep_threshold', 10)
                ON CONFLICT (key) DO NOTHING
            ''')
        
        logger.info("[Database] PostgreSQL tables initialized")
    
    # ========== ECONOMY METHODS ==========
    
    async def get_user_economy(self, user_id: str) -> dict:
        """Get economy data for a user."""
        async with self.pool.acquire() as conn:
            row = await conn.fetchrow(
                'SELECT wallet, bank, donations, last_daily, daily_streak FROM economy WHERE user_id = $1',
                user_id
            )
            
            if not row:
                return {"wallet": 0, "bank": 0, "donations": {}, "last_daily": None, "daily_streak": 0}
            
            donations = row['donations'] if isinstance(row['donations'], dict) else json.loads(row['donations'] or '{}')
            return {
                "wallet": row['wallet'],
                "bank": row['bank'],
                "donations": donations,
                "last_daily": row['last_daily'],
                "daily_streak": row['daily_streak'] or 0
            }
    
    async def set_user_economy(self, user_id: str, wallet: int, bank: int, donations: dict, last_daily: str = None, daily_streak: int = 0):
        """Set economy data for a user."""
        now = datetime.now(timezone.utc).timestamp()
        donations_json = json.dumps(donations)
        
        async with self.pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO economy (user_id, wallet, bank, donations, last_daily, daily_streak, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7)
                ON CONFLICT (user_id) DO UPDATE
                SET wallet = $2, bank = $3, donations = $4, last_daily = $5, daily_streak = $6, updated_at = $7
            ''', user_id, wallet, bank, donations_json, last_daily, daily_streak, now)
    
    async def update_balance(self, user_id: str, wallet_delta: int = 0, bank_delta: int = 0):
        """Update wallet and/or bank by delta amounts."""
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO economy (user_id, wallet, bank, updated_at)
                VALUES ($1, GREATEST(0, $2), GREATEST(0, $3), $4)
                ON CONFLICT (user_id) DO UPDATE
                SET wallet = GREATEST(0, economy.wallet + $2),
                    bank = GREATEST(0, economy.bank + $3),
                    updated_at = $4
            ''', user_id, wallet_delta, bank_delta, now)
    
    async def get_all_users_economy(self) -> dict:
        """Get all user economy data."""
        async with self.pool.acquire() as conn:
            rows = await conn.fetch('SELECT user_id, wallet, bank, donations, last_daily, daily_streak FROM economy')
            
            result = {}
            for row in rows:
                donations = row['donations'] if isinstance(row['donations'], dict) else json.loads(row['donations'] or '{}')
                result[row['user_id']] = {
                    "wallet": row['wallet'],
                    "bank": row['bank'],
                    "donations": donations,
                    "last_daily": row['last_daily'],
                    "daily_streak": row['daily_streak'] or 0
                }
            return result
    
    # ========== INVENTORY METHODS ==========
    
    async def get_inventory(self, user_id: str) -> dict:
        """Get inventory for a user."""
        async with self.pool.acquire() as conn:
            rows = await conn.fetch(
                'SELECT item_id, quantity FROM inventory WHERE user_id = $1 AND quantity > 0',
                user_id
            )
            return {row['item_id']: row['quantity'] for row in rows}
    
    async def update_inventory(self, user_id: str, item_id: str, quantity_delta: int):
        """Update inventory quantity by delta."""
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO inventory (user_id, item_id, quantity, updated_at)
                VALUES ($1, $2, GREATEST(0, $3), $4)
                ON CONFLICT (user_id, item_id) DO UPDATE
                SET quantity = GREATEST(0, inventory.quantity + $3), updated_at = $4
            ''', user_id, item_id, quantity_delta, now)
    
    # ========== POKEMON METHODS ==========
    
    async def get_owned_pokemon(self, owner_id: str) -> list:
        """Get all pokemon owned by a user."""
        async with self.pool.acquire() as conn:
            rows = await conn.fetch('''
                SELECT pokemon_id, species_id, nickname, rarity, level, xp, ivs_json, trait, 
                       current_hp, is_fainted, is_equipped, created_at
                FROM pokemon_owned WHERE owner_id = $1
            ''', owner_id)
            
            return [{
                "pokemon_id": row['pokemon_id'],
                "species_id": row['species_id'],
                "nickname": row['nickname'],
                "rarity": row['rarity'],
                "level": row['level'],
                "xp": row['xp'],
                "ivs": row['ivs_json'] if isinstance(row['ivs_json'], dict) else json.loads(row['ivs_json'] or '{}'),
                "trait": row['trait'],
                "current_hp": row['current_hp'],
                "is_fainted": bool(row['is_fainted']),
                "is_equipped": bool(row['is_equipped']),
                "created_at": row['created_at']
            } for row in rows]
    
    async def add_pokemon(self, pokemon_id: str, owner_id: str, species_id: str, rarity: str,
                         level: int = 1, xp: int = 0, ivs: dict = None, trait: str = None,
                         current_hp: int = 0, nickname: str = None, is_equipped: bool = False):
        """Add a new pokemon."""
        now = datetime.now(timezone.utc).timestamp()
        ivs_json = json.dumps(ivs or {})
        
        async with self.pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO pokemon_owned (pokemon_id, owner_id, species_id, nickname, rarity, level, xp,
                                          ivs_json, trait, current_hp, is_fainted, is_equipped, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, 0, $11, $12)
            ''', pokemon_id, owner_id, species_id, nickname, rarity, level, xp,
                ivs_json, trait, current_hp, int(is_equipped), now)
    
    # ========== CACHE METHODS ==========
    
    async def get_cached_category(self, msg_hash: str, guild_id: int = None) -> Optional[str]:
        """Get cached category for a message hash."""
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 24 * 3600)
        
        async with self.pool.acquire() as conn:
            row = await conn.fetchrow('''
                SELECT category FROM message_cache
                WHERE msg_hash = $1 AND created_at > $2
            ''', msg_hash, seven_days_ago)
            
            if row:
                # Update hit count
                await conn.execute('''
                    UPDATE message_cache SET hit_count = hit_count + 1, last_hit = $1
                    WHERE msg_hash = $2
                ''', now, msg_hash)
                return row['category']
            return None
    
    async def cache_category(self, msg_hash: str, category: str, guild_id = None, content_words: list = None):
        """Cache a message hash to category mapping."""
        now = datetime.now(timezone.utc).timestamp()
        guild_str = str(guild_id) if guild_id else 'global'
        words_json = json.dumps(content_words) if content_words else None
        
        async with self.pool.acquire() as conn:
            # Check cache size
            count = await conn.fetchval('SELECT COUNT(*) FROM message_cache WHERE guild_id = $1', guild_str)
            
            if count >= 5000:
                # Evict least used
                await conn.execute('''
                    DELETE FROM message_cache WHERE msg_hash = (
                        SELECT msg_hash FROM message_cache WHERE guild_id = $1
                        ORDER BY hit_count ASC, last_hit ASC LIMIT 1
                    )
                ''', guild_str)
            
            await conn.execute('''
                INSERT INTO message_cache (msg_hash, guild_id, category, words_json, hit_count, created_at, last_hit)
                VALUES ($1, $2, $3, $4, 1, $5, $5)
                ON CONFLICT (msg_hash) DO UPDATE
                SET category = $3, hit_count = message_cache.hit_count + 1, last_hit = $5
            ''', msg_hash, guild_str, category, words_json, now)
    
    # ========== MISGENDERING MEMORY METHODS ==========
    
    async def record_misgendering(self, user_id: str, guild_id: str, term_used: str):
        """Record when someone misgenders Sonarr."""
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO misgendering_memory (user_id, guild_id, term_used, timestamp)
                VALUES ($1, $2, $3, $4)
                ON CONFLICT (user_id, guild_id) DO UPDATE
                SET term_used = $3, timestamp = $4
            ''', user_id, guild_id, term_used, now)
    
    async def get_misgendered_users(self, guild_id: str, hours_back: int = 24) -> list:
        """Get users who misgendered Sonarr recently."""
        cutoff = datetime.now(timezone.utc).timestamp() - (hours_back * 3600)
        
        async with self.pool.acquire() as conn:
            rows = await conn.fetch('''
                SELECT user_id, term_used, timestamp FROM misgendering_memory
                WHERE guild_id = $1 AND timestamp > $2
                ORDER BY timestamp DESC
            ''', guild_id, cutoff)
            
            return [{
                "user_id": row['user_id'],
                "term_used": row['term_used'],
                "timestamp": row['timestamp']
            } for row in rows]
    
    async def clear_misgendering_memory(self, user_id: str, guild_id: str):
        """Clear misgendering memory for a user."""
        async with self.pool.acquire() as conn:
            await conn.execute('''
                DELETE FROM misgendering_memory WHERE user_id = $1 AND guild_id = $2
            ''', user_id, guild_id)
    
    async def cleanup_expired_misgendering(self, hours_back: int = 24):
        """Clean up old misgendering records."""
        cutoff = datetime.now(timezone.utc).timestamp() - (hours_back * 3600)
        
        async with self.pool.acquire() as conn:
            await conn.execute('DELETE FROM misgendering_memory WHERE timestamp <= $1', cutoff)
    
    # ========== LOAN METHODS ==========
    
    async def get_loan(self, user_id: str) -> Optional[dict]:
        """Get active loan for a user."""
        async with self.pool.acquire() as conn:
            row = await conn.fetchrow('''
                SELECT principal, interest_rate, amount_owed, deadline_timestamp, created_timestamp,
                       status, collateral_pokemon_id, last_interest_applied, late_notice_count
                FROM loans WHERE user_id = $1
            ''', user_id)
            
            if not row:
                return None
            
            return {
                "principal": row['principal'],
                "interest_rate": row['interest_rate'],
                "amount_owed": row['amount_owed'],
                "deadline_timestamp": row['deadline_timestamp'],
                "created_timestamp": row['created_timestamp'],
                "status": row['status'],
                "collateral_pokemon_id": row['collateral_pokemon_id'],
                "last_interest_applied": row['last_interest_applied'],
                "late_notice_count": row['late_notice_count']
            }
    
    # ========== STOCK METHODS ==========
    
    async def get_all_stocks(self) -> list:
        """Get all stocks."""
        async with self.pool.acquire() as conn:
            rows = await conn.fetch('SELECT ticker, name, price, previous_price, volatility, last_updated FROM stocks')
            
            return [{
                "ticker": row['ticker'],
                "name": row['name'],
                "price": row['price'],
                "previous_price": row['previous_price'],
                "volatility": row['volatility'],
                "last_updated": row['last_updated']
            } for row in rows]
    
    async def get_portfolio(self, user_id: str) -> list:
        """Get user's stock portfolio."""
        async with self.pool.acquire() as conn:
            rows = await conn.fetch('''
                SELECT ticker, shares, avg_buy_price FROM stock_portfolio
                WHERE user_id = $1 AND shares > 0
            ''', user_id)
            
            return [{
                "ticker": row['ticker'],
                "shares": row['shares'],
                "avg_buy_price": row['avg_buy_price']
            } for row in rows]
