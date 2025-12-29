"""
Database Factory

Provides factory functions to create the appropriate database
backend based on configuration.
"""

import os
import logging
from typing import Optional

logger = logging.getLogger("bot.db")

# Global database instance
_database_instance = None


def get_database_url() -> Optional[str]:
    """Get PostgreSQL URL from environment."""
    return os.getenv('DATABASE_URL') or os.getenv('POSTGRES_URL')


def is_postgres_mode() -> bool:
    """Check if we should use PostgreSQL."""
    # Explicit HA mode
    if os.getenv('HA_ENABLED', '').lower() in ('true', '1', 'yes'):
        return True
    
    # Has Postgres URL
    if get_database_url():
        return True
    
    return False


async def create_database(database_url: str = None):
    """
    Create and initialize the appropriate database backend.
    
    Args:
        database_url: PostgreSQL connection URL (optional)
        
    Returns:
        Database instance (PostgresDatabase or SQLiteDatabase wrapper)
    """
    global _database_instance
    
    if _database_instance is not None:
        return _database_instance
    
    url = database_url or get_database_url()
    
    if url:
        # Use PostgreSQL
        from .postgres import PostgresDatabase
        
        logger.info("[Database] Using PostgreSQL backend")
        db = PostgresDatabase(url)
        await db.connect()
        _database_instance = db
        return db
    else:
        # Fall back to SQLite wrapper
        from .sqlite_compat import SQLiteDatabaseWrapper
        
        logger.info("[Database] Using SQLite backend (compatibility mode)")
        db = SQLiteDatabaseWrapper()
        await db.init()
        _database_instance = db
        return db


def get_database():
    """
    Get the current database instance.
    
    Returns:
        The database instance or None if not initialized.
    """
    return _database_instance


async def close_database():
    """Close the database connection."""
    global _database_instance
    
    if _database_instance is not None:
        if hasattr(_database_instance, 'close'):
            await _database_instance.close()
        _database_instance = None
        logger.info("[Database] Database connection closed")
