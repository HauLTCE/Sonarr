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
            self.connection = sqlite3.connect(str(DB_PATH), check_same_thread=False, timeout=10.0)
            self.connection.row_factory = sqlite3.Row
            self.cursor = self.connection.cursor()
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS economy (
                    user_id TEXT PRIMARY KEY,
                    wallet INTEGER DEFAULT 0,
                    bank INTEGER DEFAULT 0,
                    donations TEXT DEFAULT '{}',
                    last_daily TEXT,
                    daily_streak INTEGER DEFAULT 0,
                    updated_at REAL
                )
            ''')

            self.cursor.execute("PRAGMA table_info(economy)")
            columns = {row[1] for row in self.cursor.fetchall()}
            if "daily_streak" not in columns:
                self.cursor.execute("ALTER TABLE economy ADD COLUMN daily_streak INTEGER DEFAULT 0")
                self.connection.commit()
            


            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS inventory (
                    user_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    quantity INTEGER DEFAULT 0,
                    updated_at REAL,
                    PRIMARY KEY (user_id, item_id)
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS active_buffs (
                    user_id TEXT NOT NULL,
                    buff_id TEXT NOT NULL,
                    value REAL DEFAULT 0,
                    expires_at REAL NOT NULL,
                    PRIMARY KEY (user_id, buff_id)
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_owned (
                    pokemon_id TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    species_id TEXT NOT NULL,
                    nickname TEXT,
                    rarity TEXT NOT NULL,
                    level INTEGER DEFAULT 1,
                    xp INTEGER DEFAULT 0,
                    ivs_json TEXT DEFAULT '{}',
                    trait TEXT,
                    current_hp INTEGER DEFAULT 0,
                    is_fainted INTEGER DEFAULT 0,
                    is_equipped INTEGER DEFAULT 0,
                    created_at REAL
                )
            ''')

            self.cursor.execute('''
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

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_daily (
                    owner_id TEXT PRIMARY KEY,
                    last_claim TEXT
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_pokedex (
                    owner_id TEXT NOT NULL,
                    species_id TEXT NOT NULL,
                    caught INTEGER DEFAULT 0,
                    first_caught REAL,
                    PRIMARY KEY (owner_id, species_id)
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_hunt_cooldowns (
                    owner_id TEXT NOT NULL,
                    zone_id TEXT NOT NULL,
                    next_available_at REAL,
                    PRIMARY KEY (owner_id, zone_id)
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_daycare (
                    pokemon_id TEXT PRIMARY KEY,
                    owner_id TEXT NOT NULL,
                    start_at REAL,
                    end_at REAL,
                    xp_per_hour INTEGER,
                    cost INTEGER
                )
            ''')

            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_weekly (
                    owner_id TEXT PRIMARY KEY,
                    week_start TEXT,
                    hunts INTEGER DEFAULT 0,
                    catches INTEGER DEFAULT 0,
                    levels INTEGER DEFAULT 0,
                    reward_claimed INTEGER DEFAULT 0
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS pokemon_capacity (
                    owner_id TEXT PRIMARY KEY,
                    extra_slots INTEGER DEFAULT 0
                )
            ''')

            self.cursor.execute("PRAGMA table_info(pokemon_owned)")
            pokemon_owned_columns = {row[1] for row in self.cursor.fetchall()}
            if "ivs_json" not in pokemon_owned_columns:
                self.cursor.execute("ALTER TABLE pokemon_owned ADD COLUMN ivs_json TEXT DEFAULT '{}'")
            if "trait" not in pokemon_owned_columns:
                self.cursor.execute("ALTER TABLE pokemon_owned ADD COLUMN trait TEXT")
            if "current_hp" not in pokemon_owned_columns:
                self.cursor.execute("ALTER TABLE pokemon_owned ADD COLUMN current_hp INTEGER DEFAULT 0")
            if "is_fainted" not in pokemon_owned_columns:
                self.cursor.execute("ALTER TABLE pokemon_owned ADD COLUMN is_fainted INTEGER DEFAULT 0")

            self.cursor.execute("PRAGMA table_info(pokemon_encounters)")
            encounter_columns = {row[1] for row in self.cursor.fetchall()}
            if "current_hp" not in encounter_columns:
                self.cursor.execute("ALTER TABLE pokemon_encounters ADD COLUMN current_hp INTEGER DEFAULT 0")
            if "max_hp" not in encounter_columns:
                self.cursor.execute("ALTER TABLE pokemon_encounters ADD COLUMN max_hp INTEGER DEFAULT 0")
            if "zone_id" not in encounter_columns:
                self.cursor.execute("ALTER TABLE pokemon_encounters ADD COLUMN zone_id TEXT")
            
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
            
            # Response cache - stores AI-generated responses
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS response_cache (
                    msg_hash TEXT PRIMARY KEY,
                    response TEXT NOT NULL,
                    hit_count INTEGER DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_hit REAL NOT NULL
                )
            ''')
            
            # Per-guild cache threshold config
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
            
            # Migration: Add guild_id column if it doesn't exist (for older databases)
            try:
                self.cursor.execute('SELECT guild_id FROM message_cache LIMIT 1')
            except sqlite3.OperationalError:
                logger.info("[Database] Migrating message_cache: adding guild_id column")
                self.cursor.execute('ALTER TABLE message_cache ADD COLUMN guild_id TEXT DEFAULT "global"')
            
            self.connection.commit()
            logger.info("[Database] SQLite initialized successfully")
        except Exception as e:
            logger.error(f"[Database] Initialization error: {e}")
            raise
    
    def get_user_economy(self, user_id: str):
        """Get economy data for a user."""
        self.cursor.execute(
            'SELECT wallet, bank, donations, last_daily, daily_streak FROM economy WHERE user_id = ?',
            (user_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return {"wallet": 0, "bank": 0, "donations": {}, "last_daily": None, "daily_streak": 0}
        
        donations = json.loads(row[2]) if row[2] else {}
        return {
            "wallet": row[0],
            "bank": row[1],
            "donations": donations,
            "last_daily": row[3],
            "daily_streak": row[4] if row[4] is not None else 0
        }

    def user_economy_exists(self, user_id: str):
        """Check if a user has an economy row."""
        self.cursor.execute('SELECT 1 FROM economy WHERE user_id = ? LIMIT 1', (user_id,))
        return self.cursor.fetchone() is not None
    
    def set_user_economy(self, user_id: str, wallet: int, bank: int, donations: dict, last_daily: str = None, daily_streak: int = 0):
        """Update economy data for a user."""
        donations_json = json.dumps(donations)
        now = datetime.now(timezone.utc).timestamp()
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO economy 
            (user_id, wallet, bank, donations, last_daily, daily_streak, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ''', (user_id, wallet, bank, donations_json, last_daily, daily_streak, now))
        
        self.connection.commit()
    
    def update_balance(self, user_id: str, wallet_delta: int = 0, bank_delta: int = 0):
        """Update wallet and/or bank by delta amounts (faster than full set)."""
        current = self.get_user_economy(user_id)
        new_wallet = max(0, current["wallet"] + wallet_delta)
        new_bank = max(0, current["bank"] + bank_delta)
        
        self.set_user_economy(user_id, new_wallet, new_bank, current["donations"], current["last_daily"], current.get("daily_streak", 0))
    
    def get_all_users_economy(self):
        """Get all user economy data (for startup/backup)."""
        self.cursor.execute('SELECT user_id, wallet, bank, donations, last_daily, daily_streak FROM economy')
        rows = self.cursor.fetchall()
        
        result = {}
        for row in rows:
            donations = json.loads(row[3]) if row[3] else {}
            result[row[0]] = {
                "wallet": row[1],
                "bank": row[2],
                "donations": donations,
                "last_daily": row[4],
                "daily_streak": row[5] if row[5] is not None else 0
            }
        return result

    def get_inventory(self, user_id: str):
        """Get inventory items for a user."""
        self.cursor.execute(
            'SELECT item_id, quantity FROM inventory WHERE user_id = ? AND quantity > 0',
            (user_id,)
        )
        rows = self.cursor.fetchall()
        return {row[0]: row[1] for row in rows}

    def get_inventory_item(self, user_id: str, item_id: str):
        """Get a specific inventory item quantity."""
        self.cursor.execute(
            'SELECT quantity FROM inventory WHERE user_id = ? AND item_id = ?',
            (user_id, item_id)
        )
        row = self.cursor.fetchone()
        return row[0] if row else 0

    def update_inventory(self, user_id: str, item_id: str, quantity_delta: int):
        """Update inventory quantity by delta."""
        current_qty = self.get_inventory_item(user_id, item_id)
        new_qty = max(0, current_qty + quantity_delta)
        now = datetime.now(timezone.utc).timestamp()

        if new_qty == 0:
            self.cursor.execute(
                'DELETE FROM inventory WHERE user_id = ? AND item_id = ?',
                (user_id, item_id)
            )
        else:
            self.cursor.execute('''
                INSERT OR REPLACE INTO inventory (user_id, item_id, quantity, updated_at)
                VALUES (?, ?, ?, ?)
            ''', (user_id, item_id, new_qty, now))

        self.connection.commit()
        return new_qty

    def set_active_buff(self, user_id: str, buff_id: str, value: float, duration_seconds: int):
        """Set or refresh an active buff for a user."""
        expires_at = datetime.now(timezone.utc).timestamp() + duration_seconds
        self.cursor.execute('''
            INSERT OR REPLACE INTO active_buffs (user_id, buff_id, value, expires_at)
            VALUES (?, ?, ?, ?)
        ''', (user_id, buff_id, value, expires_at))
        self.connection.commit()

    def get_active_buff(self, user_id: str, buff_id: str):
        """Get active buff value if not expired."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute(
            'SELECT value, expires_at FROM active_buffs WHERE user_id = ? AND buff_id = ?',
            (user_id, buff_id)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        value, expires_at = row[0], row[1]
        if expires_at <= now:
            self.clear_active_buff(user_id, buff_id)
            return None
        return value

    def get_active_buffs(self, user_id: str):
        """Get all active buffs for a user."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute(
            'SELECT buff_id, value, expires_at FROM active_buffs WHERE user_id = ?',
            (user_id,)
        )
        rows = self.cursor.fetchall()
        active = []
        expired_ids = []
        for row in rows:
            buff_id, value, expires_at = row[0], row[1], row[2]
            if expires_at <= now:
                expired_ids.append(buff_id)
                continue
            active.append({
                "buff_id": buff_id,
                "value": value,
                "expires_at": expires_at,
            })
        for buff_id in expired_ids:
            self.clear_active_buff(user_id, buff_id)
        return active

    def clear_active_buff(self, user_id: str, buff_id: str):
        """Remove a buff for a user."""
        self.cursor.execute(
            'DELETE FROM active_buffs WHERE user_id = ? AND buff_id = ?',
            (user_id, buff_id)
        )
        self.connection.commit()

    def cleanup_expired_buffs(self):
        """Remove expired buffs."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('DELETE FROM active_buffs WHERE expires_at <= ?', (now,))
        self.connection.commit()

    def get_owned_pokemon(self, owner_id: str):
        """Get all pokemon owned by a user."""
        self.cursor.execute(
            '''
            SELECT pokemon_id, species_id, nickname, rarity, level, xp, ivs_json, trait, current_hp, is_fainted, is_equipped, created_at
            FROM pokemon_owned
            WHERE owner_id = ?
            ORDER BY created_at
            ''',
            (owner_id,)
        )
        rows = self.cursor.fetchall()
        return [
            {
                "pokemon_id": row[0],
                "species_id": row[1],
                "nickname": row[2],
                "rarity": row[3],
                "level": row[4],
                "xp": row[5],
                "ivs": json.loads(row[6]) if row[6] else {},
                "trait": row[7],
                "current_hp": row[8],
                "is_fainted": bool(row[9]),
                "is_equipped": bool(row[10]),
                "created_at": row[11],
            }
            for row in rows
        ]

    def count_owned_pokemon(self, owner_id: str):
        """Count how many pokemon a user owns."""
        self.cursor.execute(
            'SELECT COUNT(1) FROM pokemon_owned WHERE owner_id = ?',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        return int(row[0]) if row else 0

    def get_pokemon_by_id(self, owner_id: str, pokemon_id: str):
        """Get a single pokemon by id for a user."""
        self.cursor.execute(
            '''
            SELECT pokemon_id, species_id, nickname, rarity, level, xp, ivs_json, trait, current_hp, is_fainted, is_equipped, created_at
            FROM pokemon_owned
            WHERE owner_id = ? AND pokemon_id = ?
            LIMIT 1
            ''',
            (owner_id, pokemon_id)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "pokemon_id": row[0],
            "species_id": row[1],
            "nickname": row[2],
            "rarity": row[3],
            "level": row[4],
            "xp": row[5],
            "ivs": json.loads(row[6]) if row[6] else {},
            "trait": row[7],
            "current_hp": row[8],
            "is_fainted": bool(row[9]),
            "is_equipped": bool(row[10]),
            "created_at": row[11],
        }

    def add_pokemon(
        self,
        pokemon_id: str,
        owner_id: str,
        species_id: str,
        rarity: str,
        level: int = 1,
        xp: int = 0,
        ivs: dict = None,
        trait: str = None,
        current_hp: int = 0,
        nickname: str = None,
        is_equipped: bool = False
    ):
        """Add a new pokemon to a user."""
        now = datetime.now(timezone.utc).timestamp()
        ivs_json = json.dumps(ivs or {})
        self.cursor.execute(
            '''
            INSERT INTO pokemon_owned
            (pokemon_id, owner_id, species_id, nickname, rarity, level, xp, ivs_json, trait, current_hp, is_fainted, is_equipped, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''',
            (pokemon_id, owner_id, species_id, nickname, rarity, level, xp, ivs_json, trait, current_hp, 0, int(is_equipped), now)
        )
        self.connection.commit()

    def update_pokemon_progress(self, owner_id: str, pokemon_id: str, level: int, xp: int):
        """Update pokemon level and xp."""
        self.cursor.execute(
            '''
            UPDATE pokemon_owned
            SET level = ?, xp = ?
            WHERE owner_id = ? AND pokemon_id = ?
            ''',
            (level, xp, owner_id, pokemon_id)
        )
        self.connection.commit()

    def update_pokemon_hp(self, owner_id: str, pokemon_id: str, current_hp: int):
        """Update pokemon current HP."""
        is_fainted = 1 if current_hp <= 0 else 0
        self.cursor.execute(
            '''
            UPDATE pokemon_owned
            SET current_hp = ?, is_fainted = ?
            WHERE owner_id = ? AND pokemon_id = ?
            ''',
            (current_hp, is_fainted, owner_id, pokemon_id)
        )
        self.connection.commit()

    def update_pokemon_ivs_trait(self, owner_id: str, pokemon_id: str, ivs: dict, trait: str):
        """Update pokemon IVs and trait."""
        ivs_json = json.dumps(ivs or {})
        self.cursor.execute(
            '''
            UPDATE pokemon_owned
            SET ivs_json = ?, trait = ?
            WHERE owner_id = ? AND pokemon_id = ?
            ''',
            (ivs_json, trait, owner_id, pokemon_id)
        )
        self.connection.commit()

    def update_pokemon_species(self, owner_id: str, pokemon_id: str, species_id: str, rarity: str):
        """Update pokemon species (evolution)."""
        self.cursor.execute(
            '''
            UPDATE pokemon_owned
            SET species_id = ?, rarity = ?
            WHERE owner_id = ? AND pokemon_id = ?
            ''',
            (species_id, rarity, owner_id, pokemon_id)
        )
        self.connection.commit()

    def set_equipped_pokemon(self, owner_id: str, pokemon_id: str):
        """Equip a pokemon (only one can be equipped)."""
        self.cursor.execute(
            'UPDATE pokemon_owned SET is_equipped = 0 WHERE owner_id = ?',
            (owner_id,)
        )
        self.cursor.execute(
            'UPDATE pokemon_owned SET is_equipped = 1 WHERE owner_id = ? AND pokemon_id = ?',
            (owner_id, pokemon_id)
        )
        self.connection.commit()

    def clear_equipped_pokemon(self, owner_id: str):
        """Unequip any pokemon for a user."""
        self.cursor.execute(
            'UPDATE pokemon_owned SET is_equipped = 0 WHERE owner_id = ?',
            (owner_id,)
        )
        self.connection.commit()

    def get_equipped_pokemon(self, owner_id: str):
        """Get currently equipped pokemon for a user."""
        self.cursor.execute(
            '''
            SELECT pokemon_id, species_id, nickname, rarity, level, xp, ivs_json, trait, current_hp, is_fainted, is_equipped, created_at
            FROM pokemon_owned
            WHERE owner_id = ? AND is_equipped = 1
            LIMIT 1
            ''',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "pokemon_id": row[0],
            "species_id": row[1],
            "nickname": row[2],
            "rarity": row[3],
            "level": row[4],
            "xp": row[5],
            "ivs": json.loads(row[6]) if row[6] else {},
            "trait": row[7],
            "current_hp": row[8],
            "is_fainted": bool(row[9]),
            "is_equipped": bool(row[10]),
            "created_at": row[11],
        }

    def delete_pokemon(self, owner_id: str, pokemon_id: str):
        """Delete a pokemon from a user."""
        self.cursor.execute(
            'DELETE FROM pokemon_owned WHERE owner_id = ? AND pokemon_id = ?',
            (owner_id, pokemon_id)
        )
        self.connection.commit()

    def get_pokemon_encounter(self, owner_id: str):
        """Get current wild encounter for a user."""
        self.cursor.execute(
            '''
            SELECT species_id, rarity, level, current_hp, max_hp, zone_id, expires_at, attempts
            FROM pokemon_encounters
            WHERE owner_id = ?
            LIMIT 1
            ''',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "species_id": row[0],
            "rarity": row[1],
            "level": row[2],
            "current_hp": row[3],
            "max_hp": row[4],
            "zone_id": row[5],
            "expires_at": row[6],
            "attempts": row[7],
        }

    def set_pokemon_encounter(
        self,
        owner_id: str,
        species_id: str,
        rarity: str,
        level: int,
        current_hp: int,
        max_hp: int,
        zone_id: str,
        expires_at: float,
        attempts: int = 0
    ):
        """Create or replace a wild encounter for a user."""
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_encounters
            (owner_id, species_id, rarity, level, current_hp, max_hp, zone_id, expires_at, attempts)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''',
            (owner_id, species_id, rarity, level, current_hp, max_hp, zone_id, expires_at, attempts)
        )
        self.connection.commit()

    def update_pokemon_encounter_attempts(self, owner_id: str, attempts: int):
        """Update encounter attempts."""
        self.cursor.execute(
            'UPDATE pokemon_encounters SET attempts = ? WHERE owner_id = ?',
            (attempts, owner_id)
        )
        self.connection.commit()

    def update_pokemon_encounter_hp(self, owner_id: str, current_hp: int):
        """Update encounter HP."""
        self.cursor.execute(
            'UPDATE pokemon_encounters SET current_hp = ? WHERE owner_id = ?',
            (current_hp, owner_id)
        )
        self.connection.commit()

    def clear_pokemon_encounter(self, owner_id: str):
        """Clear wild encounter for a user."""
        self.cursor.execute(
            'DELETE FROM pokemon_encounters WHERE owner_id = ?',
            (owner_id,)
        )
        self.connection.commit()

    def get_pokemon_daily(self, owner_id: str):
        """Get last pokemon daily claim date."""
        self.cursor.execute(
            'SELECT last_claim FROM pokemon_daily WHERE owner_id = ?',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        return row[0] if row else None

    def set_pokemon_daily(self, owner_id: str, last_claim: str):
        """Set pokemon daily claim date."""
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_daily (owner_id, last_claim)
            VALUES (?, ?)
            ''',
            (owner_id, last_claim)
        )
        self.connection.commit()

    def record_pokedex_catch(self, owner_id: str, species_id: str):
        """Record a pokedex catch."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_pokedex (owner_id, species_id, caught, first_caught)
            VALUES (
                ?,
                ?,
                COALESCE((SELECT caught FROM pokemon_pokedex WHERE owner_id = ? AND species_id = ?), 0) + 1,
                COALESCE((SELECT first_caught FROM pokemon_pokedex WHERE owner_id = ? AND species_id = ?), ?)
            )
            ''',
            (owner_id, species_id, owner_id, species_id, owner_id, species_id, now)
        )
        self.connection.commit()

    def get_pokedex_unique_count(self, owner_id: str):
        """Get number of unique species caught."""
        self.cursor.execute(
            'SELECT COUNT(1) FROM pokemon_pokedex WHERE owner_id = ?',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        return int(row[0]) if row else 0

    def get_hunt_cooldown(self, owner_id: str, zone_id: str):
        """Get hunt cooldown time for a zone."""
        self.cursor.execute(
            'SELECT next_available_at FROM pokemon_hunt_cooldowns WHERE owner_id = ? AND zone_id = ?',
            (owner_id, zone_id)
        )
        row = self.cursor.fetchone()
        return row[0] if row else None

    def set_hunt_cooldown(self, owner_id: str, zone_id: str, next_available_at: float):
        """Set hunt cooldown time for a zone."""
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_hunt_cooldowns (owner_id, zone_id, next_available_at)
            VALUES (?, ?, ?)
            ''',
            (owner_id, zone_id, next_available_at)
        )
        self.connection.commit()

    def add_daycare_entry(self, pokemon_id: str, owner_id: str, start_at: float, end_at: float, xp_per_hour: int, cost: int):
        """Add pokemon to daycare."""
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_daycare (pokemon_id, owner_id, start_at, end_at, xp_per_hour, cost)
            VALUES (?, ?, ?, ?, ?, ?)
            ''',
            (pokemon_id, owner_id, start_at, end_at, xp_per_hour, cost)
        )
        self.connection.commit()

    def get_daycare_entries(self, owner_id: str):
        """Get daycare entries for a user."""
        self.cursor.execute(
            '''
            SELECT pokemon_id, start_at, end_at, xp_per_hour, cost
            FROM pokemon_daycare
            WHERE owner_id = ?
            ''',
            (owner_id,)
        )
        rows = self.cursor.fetchall()
        return [
            {
                "pokemon_id": row[0],
                "start_at": row[1],
                "end_at": row[2],
                "xp_per_hour": row[3],
                "cost": row[4],
            }
            for row in rows
        ]

    def remove_daycare_entry(self, pokemon_id: str):
        """Remove a daycare entry."""
        self.cursor.execute(
            'DELETE FROM pokemon_daycare WHERE pokemon_id = ?',
            (pokemon_id,)
        )
        self.connection.commit()

    def get_weekly(self, owner_id: str):
        """Get weekly progress."""
        self.cursor.execute(
            '''
            SELECT week_start, hunts, catches, levels, reward_claimed
            FROM pokemon_weekly
            WHERE owner_id = ?
            ''',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "week_start": row[0],
            "hunts": row[1],
            "catches": row[2],
            "levels": row[3],
            "reward_claimed": bool(row[4]),
        }

    def set_weekly(self, owner_id: str, week_start: str, hunts: int, catches: int, levels: int, reward_claimed: bool):
        """Set weekly progress."""
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_weekly (owner_id, week_start, hunts, catches, levels, reward_claimed)
            VALUES (?, ?, ?, ?, ?, ?)
            ''',
            (owner_id, week_start, hunts, catches, levels, int(reward_claimed))
        )
        self.connection.commit()

    def get_pokemon_capacity_bonus(self, owner_id: str):
        """Get extra pokemon slots for a user."""
        self.cursor.execute(
            'SELECT extra_slots FROM pokemon_capacity WHERE owner_id = ?',
            (owner_id,)
        )
        row = self.cursor.fetchone()
        return int(row[0]) if row else 0

    def add_pokemon_capacity_bonus(self, owner_id: str, delta: int):
        """Increase extra pokemon slots for a user."""
        current = self.get_pokemon_capacity_bonus(owner_id)
        new_total = max(0, current + int(delta))
        self.cursor.execute(
            '''
            INSERT OR REPLACE INTO pokemon_capacity (owner_id, extra_slots)
            VALUES (?, ?)
            ''',
            (owner_id, new_total)
        )
        self.connection.commit()
        return new_total
    
    def get_cached_category(self, msg_hash: str, guild_id: int = None):
        """Get cached category. Returns None if not found or expired (7 days)."""
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 86400)  # 7 days in seconds
        
        # Query with guild_id if provided, otherwise match any
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
            # Check if expired (older than 7 days)
            if row[1] < seven_days_ago:
                # Delete expired entry
                if guild_id:
                    self.cursor.execute('DELETE FROM message_cache WHERE msg_hash = ? AND guild_id = ?', (msg_hash, str(guild_id)))
                else:
                    self.cursor.execute('DELETE FROM message_cache WHERE msg_hash = ?', (msg_hash,))
                self.connection.commit()
                return None
            
            # Update hit count and last_hit
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
    
    def cache_category(self, msg_hash: str, category: str, guild_id = None):
        """Cache a message hash → category. Starts at hit_count=1. If full, evict least used."""
        now = datetime.now(timezone.utc).timestamp()
        guild_str = str(guild_id) if guild_id else "global"
        
        self.cursor.execute('SELECT COUNT(*) FROM message_cache WHERE guild_id = ?', (guild_str,))
        count = self.cursor.fetchone()[0]
        
        if count >= 5000:
            self.cursor.execute(
                'DELETE FROM message_cache WHERE msg_hash = (SELECT msg_hash FROM message_cache WHERE guild_id = ? ORDER BY hit_count ASC, last_hit ASC LIMIT 1)',
                (guild_str,)
            )
        
        # New entries start with hit_count=1
        self.cursor.execute('''
            INSERT OR REPLACE INTO message_cache (msg_hash, guild_id, category, hit_count, created_at, last_hit)
            VALUES (?, ?, ?, 1, ?, ?)
        ''', (msg_hash, guild_str, category, now, now))
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
        
        if count >= 3000:  # Lower limit for responses (they're longer)
            self.cursor.execute('DELETE FROM response_cache WHERE msg_hash = (SELECT msg_hash FROM response_cache ORDER BY hit_count ASC, last_hit ASC LIMIT 1)')
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO response_cache (msg_hash, response, hit_count, created_at, last_hit)
            VALUES (?, ?, 1, ?, ?)
        ''', (msg_hash, response, now, now))
        self.connection.commit()
    
    def fuzzy_search_category(self, content_words: list):
        """Search for cached entries that share content words. Returns best match or None."""
        if not content_words:
            return None
        
        now = datetime.now(timezone.utc).timestamp()
        seven_days_ago = now - (7 * 86400)
        
        # Get all non-expired cache entries
        self.cursor.execute(
            'SELECT msg_hash, category FROM message_cache WHERE created_at > ?',
            (seven_days_ago,)
        )
        rows = self.cursor.fetchall()
        
        if not rows:
            return None
        
        # Find entries with overlapping words (stored hash includes sorted words)
        content_set = set(content_words)
        best_match = None
        best_overlap = 0
        
        for row in rows:
            # We can't decode hash, but we can check if this exact combo exists
            # For fuzzy, we'd need to store words separately - skip for now
            pass
        
        return None  # Fuzzy search needs word storage - implement later
    
    def get_keep_threshold(self, guild_id: str = "global"):
        """Get per-guild adaptive keep threshold."""
        self.cursor.execute('SELECT threshold FROM guild_cache_config WHERE guild_id = ?', (guild_id,))
        row = self.cursor.fetchone()
        if row:
            return row[0]
        # Initialize with threshold 1 if not exists
        self.cursor.execute('INSERT OR IGNORE INTO guild_cache_config (guild_id, threshold) VALUES (?, ?)', (guild_id, 1))
        self.connection.commit()
        return 1
    
    def set_keep_threshold(self, value: int, guild_id: str = "global"):
        """Set per-guild adaptive keep threshold."""
        value = max(0, value)  # Don't go below 0
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
        
        # Step 1: Delete entries below threshold
        self.cursor.execute(
            'DELETE FROM message_cache WHERE guild_id = ? AND hit_count < ?', 
            (guild_id, threshold)
        )
        deleted = self.cursor.rowcount
        
        # Step 2: Reset hit_count to 0 for remaining entries
        self.cursor.execute(
            'UPDATE message_cache SET hit_count = 0 WHERE guild_id = ?',
            (guild_id,)
        )
        
        # Step 3: Count remaining entries
        self.cursor.execute('SELECT COUNT(*) FROM message_cache WHERE guild_id = ?', (guild_id,))
        remaining = self.cursor.fetchone()[0]
        
        # Step 4: Adjust threshold based on remaining count
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

db = Database()
