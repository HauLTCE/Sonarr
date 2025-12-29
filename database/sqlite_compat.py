"""
SQLite Database Compatibility Wrapper

Wraps the existing SQLite Database class to provide
an async-compatible interface matching PostgresDatabase.
"""

import asyncio
import logging
from typing import Optional
from functools import partial

logger = logging.getLogger("bot.db")


class SQLiteDatabaseWrapper:
    """
    Async wrapper around the existing SQLite Database class.
    
    Provides the same async interface as PostgresDatabase
    but delegates to the synchronous SQLite database.
    """
    
    def __init__(self):
        self._db = None
        self._loop = None
        self._initialized = False
    
    async def init(self):
        """Initialize the SQLite database."""
        if self._initialized:
            return
        
        # Import the existing database
        from utils.database import Database
        
        self._db = Database()
        self._loop = asyncio.get_event_loop()
        self._initialized = True
        
        logger.info("[Database] SQLite wrapper initialized")
    
    async def close(self):
        """Close the database connection."""
        if self._db and hasattr(self._db, 'close'):
            self._db.close()
        self._initialized = False
    
    def _run_sync(self, func, *args, **kwargs):
        """Run a synchronous function in the executor."""
        return self._loop.run_in_executor(None, partial(func, *args, **kwargs))
    
    # ========== ECONOMY METHODS ==========
    
    async def get_user_economy(self, user_id: str) -> dict:
        return await self._run_sync(self._db.get_user_economy, user_id)
    
    async def set_user_economy(self, user_id: str, wallet: int, bank: int, donations: dict, 
                               last_daily: str = None, daily_streak: int = 0):
        return await self._run_sync(self._db.set_user_economy, user_id, wallet, bank, 
                                   donations, last_daily, daily_streak)
    
    async def update_balance(self, user_id: str, wallet_delta: int = 0, bank_delta: int = 0):
        return await self._run_sync(self._db.update_balance, user_id, wallet_delta, bank_delta)
    
    async def get_all_users_economy(self) -> dict:
        return await self._run_sync(self._db.get_all_users_economy)
    
    # ========== INVENTORY METHODS ==========
    
    async def get_inventory(self, user_id: str) -> dict:
        return await self._run_sync(self._db.get_inventory, user_id)
    
    async def update_inventory(self, user_id: str, item_id: str, quantity_delta: int):
        return await self._run_sync(self._db.update_inventory, user_id, item_id, quantity_delta)
    
    # ========== POKEMON METHODS ==========
    
    async def get_owned_pokemon(self, owner_id: str) -> list:
        return await self._run_sync(self._db.get_owned_pokemon, owner_id)
    
    async def add_pokemon(self, pokemon_id: str, owner_id: str, species_id: str, rarity: str,
                         level: int = 1, xp: int = 0, ivs: dict = None, trait: str = None,
                         current_hp: int = 0, nickname: str = None, is_equipped: bool = False):
        return await self._run_sync(self._db.add_pokemon, pokemon_id, owner_id, species_id, 
                                   rarity, level, xp, ivs, trait, current_hp, nickname, is_equipped)
    
    # ========== CACHE METHODS ==========
    
    async def get_cached_category(self, msg_hash: str, guild_id: int = None) -> Optional[str]:
        return await self._run_sync(self._db.get_cached_category, msg_hash, guild_id)
    
    async def cache_category(self, msg_hash: str, category: str, guild_id = None, content_words: list = None):
        return await self._run_sync(self._db.cache_category, msg_hash, category, guild_id, content_words)
    
    # ========== MISGENDERING MEMORY METHODS ==========
    
    async def record_misgendering(self, user_id: str, guild_id: str, term_used: str):
        return await self._run_sync(self._db.record_misgendering, user_id, guild_id, term_used)
    
    async def get_misgendered_users(self, guild_id: str, hours_back: int = 24) -> list:
        return await self._run_sync(self._db.get_misgendered_users, guild_id, hours_back)
    
    async def clear_misgendering_memory(self, user_id: str, guild_id: str):
        return await self._run_sync(self._db.clear_misgendering_memory, user_id, guild_id)
    
    async def cleanup_expired_misgendering(self, hours_back: int = 24):
        return await self._run_sync(self._db.cleanup_expired_misgendering, hours_back)
    
    # ========== LOAN METHODS ==========
    
    async def get_loan(self, user_id: str):
        return await self._run_sync(self._db.get_loan, user_id)
    
    # ========== STOCK METHODS ==========
    
    async def get_all_stocks(self) -> list:
        return await self._run_sync(self._db.get_all_stocks)
    
    async def get_portfolio(self, user_id: str) -> list:
        return await self._run_sync(self._db.get_portfolio, user_id)
    
    # ========== PASSTHROUGH FOR OTHER METHODS ==========
    # These are called directly on the underlying db
    
    def __getattr__(self, name):
        """Passthrough for any methods not explicitly wrapped."""
        if self._db is None:
            raise RuntimeError("Database not initialized")
        return getattr(self._db, name)
