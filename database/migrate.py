"""
SQLite to PostgreSQL Migration Script

Run this script to migrate all data from SQLite to PostgreSQL.
Requires: DATABASE_URL environment variable or pass as argument.

Usage:
    python -m database.migrate
    python -m database.migrate postgresql://user:pass@host/db
"""

import asyncio
import json
import os
import sys
import sqlite3
from pathlib import Path

try:
    import asyncpg
except ImportError:
    print("ERROR: asyncpg is required. Install with: pip install asyncpg")
    sys.exit(1)


SQLITE_PATH = Path(__file__).parent.parent / "bot_data.db"


async def migrate(database_url: str):
    """Migrate all data from SQLite to PostgreSQL."""
    
    if not SQLITE_PATH.exists():
        print(f"ERROR: SQLite database not found at {SQLITE_PATH}")
        sys.exit(1)
    
    print(f"[Migration] Source: {SQLITE_PATH}")
    print(f"[Migration] Target: PostgreSQL")
    print()
    
    # Connect to SQLite
    sqlite_conn = sqlite3.connect(SQLITE_PATH)
    sqlite_conn.row_factory = sqlite3.Row
    
    # Connect to PostgreSQL
    pg_pool = await asyncpg.create_pool(database_url, min_size=1, max_size=5)
    
    try:
        async with pg_pool.acquire() as pg:
            # Initialize PostgreSQL tables first
            from .postgres import PostgresDatabase
            temp_db = PostgresDatabase(database_url)
            temp_db.pool = pg_pool
            await temp_db._init_tables()
            
            print("[Migration] Tables initialized")
            print()
            
            # ========== ECONOMY ==========
            print("[Migration] Migrating economy data...")
            cursor = sqlite_conn.execute("SELECT * FROM economy")
            rows = cursor.fetchall()
            
            for row in rows:
                donations = row['donations'] if row['donations'] else '{}'
                await pg.execute('''
                    INSERT INTO economy (user_id, wallet, bank, donations, last_daily, daily_streak, updated_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7)
                    ON CONFLICT (user_id) DO NOTHING
                ''', row['user_id'], row['wallet'] or 0, row['bank'] or 0,
                    donations, row['last_daily'], row['daily_streak'] or 0, row['updated_at'])
            
            print(f"  ✓ {len(rows)} economy records")
            
            # ========== INVENTORY ==========
            print("[Migration] Migrating inventory data...")
            cursor = sqlite_conn.execute("SELECT * FROM inventory")
            rows = cursor.fetchall()
            
            for row in rows:
                await pg.execute('''
                    INSERT INTO inventory (user_id, item_id, quantity, updated_at)
                    VALUES ($1, $2, $3, $4)
                    ON CONFLICT (user_id, item_id) DO NOTHING
                ''', row['user_id'], row['item_id'], row['quantity'] or 0, row['updated_at'])
            
            print(f"  ✓ {len(rows)} inventory records")
            
            # ========== POKEMON OWNED ==========
            print("[Migration] Migrating pokemon data...")
            cursor = sqlite_conn.execute("SELECT * FROM pokemon_owned")
            rows = cursor.fetchall()
            
            for row in rows:
                ivs = row['ivs_json'] if row['ivs_json'] else '{}'
                await pg.execute('''
                    INSERT INTO pokemon_owned (pokemon_id, owner_id, species_id, nickname, rarity, 
                                              level, xp, ivs_json, trait, current_hp, is_fainted, 
                                              is_equipped, created_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
                    ON CONFLICT (pokemon_id) DO NOTHING
                ''', row['pokemon_id'], row['owner_id'], row['species_id'], row['nickname'],
                    row['rarity'], row['level'] or 1, row['xp'] or 0, ivs, row['trait'],
                    row['current_hp'] or 0, row['is_fainted'] or 0, row['is_equipped'] or 0,
                    row['created_at'])
            
            print(f"  ✓ {len(rows)} pokemon records")
            
            # ========== POKEMON ENCOUNTERS ==========
            print("[Migration] Migrating pokemon encounters...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM pokemon_encounters")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO pokemon_encounters (owner_id, species_id, rarity, level, 
                                                       current_hp, max_hp, zone_id, expires_at, attempts)
                        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                        ON CONFLICT (owner_id) DO NOTHING
                    ''', row['owner_id'], row['species_id'], row['rarity'], row['level'],
                        row['current_hp'] or 0, row['max_hp'] or 0, row['zone_id'],
                        row['expires_at'], row['attempts'] or 0)
                
                print(f"  ✓ {len(rows)} encounter records")
            except sqlite3.OperationalError:
                print("  - No encounters table")
            
            # ========== POKEMON POKEDEX ==========
            print("[Migration] Migrating pokedex data...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM pokemon_pokedex")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO pokemon_pokedex (owner_id, species_id, caught, first_caught)
                        VALUES ($1, $2, $3, $4)
                        ON CONFLICT (owner_id, species_id) DO NOTHING
                    ''', row['owner_id'], row['species_id'], row['caught'] or 0, row['first_caught'])
                
                print(f"  ✓ {len(rows)} pokedex records")
            except sqlite3.OperationalError:
                print("  - No pokedex table")
            
            # ========== LOANS ==========
            print("[Migration] Migrating loans...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM loans")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO loans (user_id, principal, interest_rate, amount_owed, 
                                          deadline_timestamp, created_timestamp, status,
                                          collateral_pokemon_id, last_interest_applied, late_notice_count)
                        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                        ON CONFLICT (user_id) DO NOTHING
                    ''', row['user_id'], row['principal'], row['interest_rate'], row['amount_owed'],
                        row['deadline_timestamp'], row['created_timestamp'], row['status'] or 'active',
                        row['collateral_pokemon_id'], row['last_interest_applied'], row['late_notice_count'] or 0)
                
                print(f"  ✓ {len(rows)} loan records")
            except sqlite3.OperationalError:
                print("  - No loans table")
            
            # ========== STOCKS ==========
            print("[Migration] Migrating stocks...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM stocks")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO stocks (ticker, name, price, previous_price, volatility, last_updated)
                        VALUES ($1, $2, $3, $4, $5, $6)
                        ON CONFLICT (ticker) DO NOTHING
                    ''', row['ticker'], row['name'], row['price'], row['previous_price'],
                        row['volatility'] or 'medium', row['last_updated'])
                
                print(f"  ✓ {len(rows)} stock records")
            except sqlite3.OperationalError:
                print("  - No stocks table")
            
            # ========== STOCK PORTFOLIO ==========
            print("[Migration] Migrating portfolios...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM stock_portfolio")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO stock_portfolio (user_id, ticker, shares, avg_buy_price)
                        VALUES ($1, $2, $3, $4)
                        ON CONFLICT (user_id, ticker) DO NOTHING
                    ''', row['user_id'], row['ticker'], row['shares'], row['avg_buy_price'])
                
                print(f"  ✓ {len(rows)} portfolio records")
            except sqlite3.OperationalError:
                print("  - No portfolio table")
            
            # ========== LEVELS ==========
            print("[Migration] Migrating levels...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM levels")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO levels (user_id, xp, level, last_message_time)
                        VALUES ($1, $2, $3, $4)
                        ON CONFLICT (user_id) DO NOTHING
                    ''', row['user_id'], row['xp'] or 0, row['level'] or 1, row['last_message_time'])
                
                print(f"  ✓ {len(rows)} level records")
            except sqlite3.OperationalError:
                print("  - No levels table")
            
            # ========== CACHE ==========
            print("[Migration] Migrating message cache...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM message_cache")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO message_cache (msg_hash, guild_id, category, words_json, 
                                                  hit_count, created_at, last_hit)
                        VALUES ($1, $2, $3, $4, $5, $6, $7)
                        ON CONFLICT (msg_hash) DO NOTHING
                    ''', row['msg_hash'], row['guild_id'] or 'global', row['category'],
                        row['words_json'], row['hit_count'] or 1, row['created_at'], row['last_hit'])
                
                print(f"  ✓ {len(rows)} cache records")
            except sqlite3.OperationalError:
                print("  - No cache table")
            
            # ========== MISGENDERING MEMORY ==========
            print("[Migration] Migrating misgendering memory...")
            try:
                cursor = sqlite_conn.execute("SELECT * FROM misgendering_memory")
                rows = cursor.fetchall()
                
                for row in rows:
                    await pg.execute('''
                        INSERT INTO misgendering_memory (user_id, guild_id, term_used, timestamp)
                        VALUES ($1, $2, $3, $4)
                        ON CONFLICT (user_id, guild_id) DO NOTHING
                    ''', row['user_id'], row['guild_id'], row['term_used'], row['timestamp'])
                
                print(f"  ✓ {len(rows)} misgendering records")
            except sqlite3.OperationalError:
                print("  - No misgendering table")
            
            print()
            print("=" * 50)
            print("[Migration] COMPLETE!")
            print("=" * 50)
            
    finally:
        sqlite_conn.close()
        await pg_pool.close()


def main():
    """Main entry point."""
    database_url = None
    
    if len(sys.argv) > 1:
        database_url = sys.argv[1]
    else:
        database_url = os.getenv('DATABASE_URL') or os.getenv('POSTGRES_URL')
    
    if not database_url:
        print("ERROR: No database URL provided.")
        print("Set DATABASE_URL environment variable or pass as argument.")
        print()
        print("Usage:")
        print("  python -m database.migrate postgresql://user:pass@host/db")
        sys.exit(1)
    
    asyncio.run(migrate(database_url))


if __name__ == '__main__':
    main()
