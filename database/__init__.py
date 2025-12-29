"""
Database Package for Sonarr Bot

This package provides database abstraction supporting:
- SQLite (local development / single instance)
- PostgreSQL (production / distributed HA)

The database layer automatically selects the appropriate backend
based on environment configuration.

Usage:
    # Auto-detect mode:
    from database import create_database, get_database
    db = await create_database()
    
    # Or explicitly for PostgreSQL:
    from database import PostgresDatabase
    db = PostgresDatabase("postgresql://user:pass@host/db")
    await db.connect()
"""

from .postgres import PostgresDatabase
from .sqlite_compat import SQLiteDatabaseWrapper
from .factory import (
    create_database, 
    get_database, 
    close_database, 
    is_postgres_mode,
    get_database_url
)

__all__ = [
    'PostgresDatabase',
    'SQLiteDatabaseWrapper', 
    'create_database',
    'get_database',
    'close_database',
    'is_postgres_mode',
    'get_database_url'
]
