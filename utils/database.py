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
            self.cursor.execute('PRAGMA journal_mode=WAL')
            self.cursor.execute('PRAGMA synchronous=NORMAL')
            self.cursor.execute('PRAGMA cache_size=10000')
            self.connection.commit()
            
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
            
            # ========== LOAN SHARK SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS loans (
                    user_id TEXT PRIMARY KEY,
                    principal INTEGER NOT NULL,
                    interest_rate REAL NOT NULL,
                    amount_owed INTEGER NOT NULL,
                    deadline_timestamp REAL NOT NULL,
                    created_timestamp REAL NOT NULL,
                    status TEXT DEFAULT 'active',
                    collateral_pokemon_id TEXT,
                    last_interest_applied REAL,
                    late_notice_count INTEGER DEFAULT 0
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS collateral_locker (
                    user_id TEXT NOT NULL,
                    pokemon_id TEXT NOT NULL,
                    repo_timestamp REAL NOT NULL,
                    buyback_cost INTEGER NOT NULL,
                    original_loan_amount INTEGER NOT NULL,
                    PRIMARY KEY (user_id, pokemon_id)
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS bankruptcies (
                    user_id TEXT PRIMARY KEY,
                    timestamp REAL NOT NULL,
                    shame_role_expires REAL NOT NULL,
                    total_debt_forgiven INTEGER DEFAULT 0,
                    can_borrow_after REAL NOT NULL
                )
            ''')
            
            # ========== STOCK MARKET SYSTEM ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS stocks (
                    ticker TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    price INTEGER NOT NULL,
                    previous_price INTEGER NOT NULL,
                    volatility TEXT DEFAULT 'medium',
                    last_updated REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS stock_portfolio (
                    user_id TEXT NOT NULL,
                    ticker TEXT NOT NULL,
                    shares INTEGER NOT NULL,
                    avg_buy_price INTEGER NOT NULL,
                    PRIMARY KEY (user_id, ticker)
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS stock_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ticker TEXT NOT NULL,
                    price INTEGER NOT NULL,
                    timestamp REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS market_news (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    headline TEXT NOT NULL,
                    affected_ticker TEXT,
                    effect TEXT,
                    timestamp REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS work_activity (
                    date TEXT PRIMARY KEY,
                    work_count INTEGER DEFAULT 0
                )
            ''')
            
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS market_channels (
                    guild_id TEXT PRIMARY KEY,
                    channel_id INTEGER NOT NULL
                )
            ''')

            # ========== MISGENDER RECORDS ==========
            self.cursor.execute('''
                CREATE TABLE IF NOT EXISTS misgender_records (
                    user_id TEXT PRIMARY KEY,
                    misgendered_at REAL NOT NULL
                )
            ''')
            
            self.cursor.execute('CREATE INDEX IF NOT EXISTS idx_loans_status ON loans(status)')
            self.cursor.execute('CREATE INDEX IF NOT EXISTS idx_loans_deadline ON loans(deadline_timestamp)')
            self.cursor.execute('CREATE INDEX IF NOT EXISTS idx_stock_history_ticker ON stock_history(ticker, timestamp DESC)')
            
            self.connection.commit()
            logger.info("[Database] SQLite initialized successfully")
        except Exception as e:
            logger.error(f"[Database] Initialization error: {e}")
            raise

    # ========== MISGENDER RECORDS API ==========
    def record_misgender(self, user_id: str):
        """Record a misgender event for a user (upserts with current timestamp)."""
        try:
            now = datetime.now(timezone.utc).timestamp()
            self.cursor.execute(
                'INSERT OR REPLACE INTO misgender_records (user_id, misgendered_at) VALUES (?, ?)',
                (user_id, now)
            )
            self.connection.commit()
            logger.info(f"[Misgender] Recorded for user {user_id} at {now}")
        except Exception as e:
            logger.error(f"[Misgender] record error: {e}")

    def clear_misgender(self, user_id: str):
        """Clear misgender record for a user (when they correctly address Sonarr)."""
        try:
            self.cursor.execute('DELETE FROM misgender_records WHERE user_id = ?', (user_id,))
            self.connection.commit()
            logger.info(f"[Misgender] Cleared for user {user_id}")
        except Exception as e:
            logger.error(f"[Misgender] clear error: {e}")

    def has_active_misgender(self, user_id: str, within_seconds: int = 86400) -> bool:
        """Check if a user has an active misgender record within the given TTL."""
        try:
            now = datetime.now(timezone.utc).timestamp()
            self.cursor.execute('SELECT misgendered_at FROM misgender_records WHERE user_id = ?', (user_id,))
            row = self.cursor.fetchone()
            if not row:
                return False
            return (now - float(row[0])) < within_seconds
        except Exception as e:
            logger.error(f"[Misgender] has_active error: {e}")
            return False

    def get_active_misgenderers(self, within_seconds: int = 86400) -> list:
        """Return list of (user_id, misgendered_at) within TTL."""
        try:
            now = datetime.now(timezone.utc).timestamp()
            cutoff = now - within_seconds
            self.cursor.execute('SELECT user_id, misgendered_at FROM misgender_records WHERE misgendered_at > ?', (cutoff,))
            rows = self.cursor.fetchall()
            return [(row[0], float(row[1])) for row in rows]
        except Exception as e:
            logger.error(f"[Misgender] get_active error: {e}")
            return []

    def cleanup_misgender_records(self, older_than_seconds: int = 86400):
        """Delete misgender records older than TTL."""
        try:
            now = datetime.now(timezone.utc).timestamp()
            cutoff = now - older_than_seconds
            self.cursor.execute('DELETE FROM misgender_records WHERE misgendered_at <= ?', (cutoff,))
            self.connection.commit()
        except Exception as e:
            logger.error(f"[Misgender] cleanup error: {e}")
    
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

    # ========== LOAN SHARK METHODS ==========
    
    def get_loan(self, user_id: str):
        """Get active loan for a user."""
        self.cursor.execute(
            'SELECT principal, interest_rate, amount_owed, deadline_timestamp, created_timestamp, status, collateral_pokemon_id, last_interest_applied, late_notice_count FROM loans WHERE user_id = ?',
            (user_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "principal": row[0],
            "interest_rate": row[1],
            "amount_owed": row[2],
            "deadline_timestamp": row[3],
            "created_timestamp": row[4],
            "status": row[5],
            "collateral_pokemon_id": row[6],
            "last_interest_applied": row[7],
            "late_notice_count": row[8]
        }
    
    def create_loan(self, user_id: str, principal: int, interest_rate: float, deadline_timestamp: float, collateral_pokemon_id: str = None):
        """Create a new loan for a user."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('''
            INSERT OR REPLACE INTO loans 
            (user_id, principal, interest_rate, amount_owed, deadline_timestamp, created_timestamp, status, collateral_pokemon_id, last_interest_applied, late_notice_count)
            VALUES (?, ?, ?, ?, ?, ?, 'active', ?, ?, 0)
        ''', (user_id, principal, interest_rate, principal, deadline_timestamp, now, collateral_pokemon_id, now))
        self.connection.commit()
    
    def update_loan_amount(self, user_id: str, new_amount: int, last_interest_applied: float = None):
        """Update the amount owed on a loan."""
        if last_interest_applied is None:
            last_interest_applied = datetime.now(timezone.utc).timestamp()
        self.cursor.execute(
            'UPDATE loans SET amount_owed = ?, last_interest_applied = ? WHERE user_id = ?',
            (new_amount, last_interest_applied, user_id)
        )
        self.connection.commit()
    
    def pay_loan(self, user_id: str, amount: int):
        """Pay down a loan. Returns remaining amount."""
        loan = self.get_loan(user_id)
        if not loan:
            return 0
        new_amount = max(0, loan["amount_owed"] - amount)
        if new_amount == 0:
            self.cursor.execute('UPDATE loans SET amount_owed = 0, status = ? WHERE user_id = ?', ('paid_off', user_id))
        else:
            self.cursor.execute('UPDATE loans SET amount_owed = ? WHERE user_id = ?', (new_amount, user_id))
        self.connection.commit()
        return new_amount
    
    def default_loan(self, user_id: str):
        """Mark a loan as defaulted."""
        self.cursor.execute('UPDATE loans SET status = ? WHERE user_id = ?', ('defaulted', user_id))
        self.connection.commit()
    
    def clear_loan(self, user_id: str):
        """Remove a loan completely."""
        self.cursor.execute('DELETE FROM loans WHERE user_id = ?', (user_id,))
        self.connection.commit()
    
    def increment_late_notice(self, user_id: str):
        """Increment late notice count."""
        self.cursor.execute('UPDATE loans SET late_notice_count = late_notice_count + 1 WHERE user_id = ?', (user_id,))
        self.connection.commit()
    
    def get_all_active_loans(self):
        """Get all active loans (for interest/enforcement tasks)."""
        self.cursor.execute(
            'SELECT user_id, principal, interest_rate, amount_owed, deadline_timestamp, created_timestamp, collateral_pokemon_id, last_interest_applied, late_notice_count FROM loans WHERE status = ?',
            ('active',)
        )
        rows = self.cursor.fetchall()
        return [{
            "user_id": row[0],
            "principal": row[1],
            "interest_rate": row[2],
            "amount_owed": row[3],
            "deadline_timestamp": row[4],
            "created_timestamp": row[5],
            "collateral_pokemon_id": row[6],
            "last_interest_applied": row[7],
            "late_notice_count": row[8]
        } for row in rows]
    
    def add_to_locker(self, user_id: str, pokemon_id: str, buyback_cost: int, original_loan_amount: int):
        """Add a Pokemon to the collateral locker."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute('''
            INSERT OR REPLACE INTO collateral_locker 
            (user_id, pokemon_id, repo_timestamp, buyback_cost, original_loan_amount)
            VALUES (?, ?, ?, ?, ?)
        ''', (user_id, pokemon_id, now, buyback_cost, original_loan_amount))
        self.connection.commit()
    
    def get_locker_items(self, user_id: str):
        """Get all items in user's collateral locker."""
        self.cursor.execute(
            'SELECT pokemon_id, repo_timestamp, buyback_cost, original_loan_amount FROM collateral_locker WHERE user_id = ?',
            (user_id,)
        )
        rows = self.cursor.fetchall()
        return [{
            "pokemon_id": row[0],
            "repo_timestamp": row[1],
            "buyback_cost": row[2],
            "original_loan_amount": row[3]
        } for row in rows]
    
    def remove_from_locker(self, user_id: str, pokemon_id: str):
        """Remove a Pokemon from the locker (after buyback)."""
        self.cursor.execute(
            'DELETE FROM collateral_locker WHERE user_id = ? AND pokemon_id = ?',
            (user_id, pokemon_id)
        )
        self.connection.commit()
    
    def record_bankruptcy(self, user_id: str, debt_forgiven: int, shame_days: int = 3, borrow_cooldown_days: int = 7):
        """Record a bankruptcy event."""
        now = datetime.now(timezone.utc).timestamp()
        shame_expires = now + (shame_days * 86400)
        can_borrow = now + (borrow_cooldown_days * 86400)
        self.cursor.execute('''
            INSERT OR REPLACE INTO bankruptcies 
            (user_id, timestamp, shame_role_expires, total_debt_forgiven, can_borrow_after)
            VALUES (?, ?, ?, ?, ?)
        ''', (user_id, now, shame_expires, debt_forgiven, can_borrow))
        self.connection.commit()
    
    def get_bankruptcy(self, user_id: str):
        """Get bankruptcy info for a user."""
        self.cursor.execute(
            'SELECT timestamp, shame_role_expires, total_debt_forgiven, can_borrow_after FROM bankruptcies WHERE user_id = ?',
            (user_id,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "timestamp": row[0],
            "shame_role_expires": row[1],
            "total_debt_forgiven": row[2],
            "can_borrow_after": row[3]
        }
    
    def is_in_shame_period(self, user_id: str):
        """Check if user is still in bankruptcy shame period."""
        bankruptcy = self.get_bankruptcy(user_id)
        if not bankruptcy:
            return False
        now = datetime.now(timezone.utc).timestamp()
        return now < bankruptcy["shame_role_expires"]
    
    def can_borrow(self, user_id: str):
        """Check if user can borrow (not in cooldown from bankruptcy)."""
        bankruptcy = self.get_bankruptcy(user_id)
        if not bankruptcy:
            return True
        now = datetime.now(timezone.utc).timestamp()
        return now >= bankruptcy["can_borrow_after"]
    
    # ========== STOCK MARKET METHODS ==========
    
    def get_stock(self, ticker: str):
        """Get stock info by ticker."""
        self.cursor.execute(
            'SELECT ticker, name, price, previous_price, volatility, last_updated FROM stocks WHERE ticker = ?',
            (ticker,)
        )
        row = self.cursor.fetchone()
        if not row:
            return None
        return {
            "ticker": row[0],
            "name": row[1],
            "price": row[2],
            "previous_price": row[3],
            "volatility": row[4],
            "last_updated": row[5]
        }
    
    def get_all_stocks(self):
        """Get all stocks."""
        self.cursor.execute('SELECT ticker, name, price, previous_price, volatility, last_updated FROM stocks')
        rows = self.cursor.fetchall()
        return [{
            "ticker": row[0],
            "name": row[1],
            "price": row[2],
            "previous_price": row[3],
            "volatility": row[4],
            "last_updated": row[5]
        } for row in rows]
    
    def upsert_stock(self, ticker: str, name: str, price: int, volatility: str = 'medium'):
        """Create or update a stock."""
        now = datetime.now(timezone.utc).timestamp()
        existing = self.get_stock(ticker)
        previous_price = existing["price"] if existing else price
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO stocks 
            (ticker, name, price, previous_price, volatility, last_updated)
            VALUES (?, ?, ?, ?, ?, ?)
        ''', (ticker, name, price, previous_price, volatility, now))
        self.connection.commit()
    
    def update_stock_price(self, ticker: str, new_price: int):
        """Update stock price and record history."""
        now = datetime.now(timezone.utc).timestamp()
        existing = self.get_stock(ticker)
        if not existing:
            return False
        
        self.cursor.execute(
            'UPDATE stocks SET previous_price = price, price = ?, last_updated = ? WHERE ticker = ?',
            (new_price, now, ticker)
        )
        
        self.cursor.execute(
            'INSERT INTO stock_history (ticker, price, timestamp) VALUES (?, ?, ?)',
            (ticker, new_price, now)
        )
        self.connection.commit()
        return True
    
    def get_stock_history(self, ticker: str, limit: int = 24):
        """Get recent price history for a stock."""
        self.cursor.execute(
            'SELECT price, timestamp FROM stock_history WHERE ticker = ? ORDER BY timestamp DESC LIMIT ?',
            (ticker, limit)
        )
        rows = self.cursor.fetchall()
        return [{"price": row[0], "timestamp": row[1]} for row in rows]
    
    def get_portfolio(self, user_id: str):
        """Get user's stock portfolio."""
        self.cursor.execute(
            'SELECT ticker, shares, avg_buy_price FROM stock_portfolio WHERE user_id = ? AND shares > 0',
            (user_id,)
        )
        rows = self.cursor.fetchall()
        return [{
            "ticker": row[0],
            "shares": row[1],
            "avg_buy_price": row[2]
        } for row in rows]
    
    def get_portfolio_position(self, user_id: str, ticker: str):
        """Get user's position in a specific stock."""
        self.cursor.execute(
            'SELECT shares, avg_buy_price FROM stock_portfolio WHERE user_id = ? AND ticker = ?',
            (user_id, ticker)
        )
        row = self.cursor.fetchone()
        if not row:
            return {"shares": 0, "avg_buy_price": 0}
        return {"shares": row[0], "avg_buy_price": row[1]}
    
    def buy_stock(self, user_id: str, ticker: str, shares: int, price_per_share: int):
        """Buy shares of a stock (updates average buy price)."""
        position = self.get_portfolio_position(user_id, ticker)
        
        old_shares = position["shares"]
        old_avg = position["avg_buy_price"]
        new_shares = old_shares + shares
        
        if new_shares > 0:
            total_old_cost = old_shares * old_avg
            total_new_cost = shares * price_per_share
            new_avg = (total_old_cost + total_new_cost) // new_shares
        else:
            new_avg = 0
        
        self.cursor.execute('''
            INSERT OR REPLACE INTO stock_portfolio (user_id, ticker, shares, avg_buy_price)
            VALUES (?, ?, ?, ?)
        ''', (user_id, ticker, new_shares, new_avg))
        self.connection.commit()
        return new_shares
    
    def sell_stock(self, user_id: str, ticker: str, shares: int):
        """Sell shares of a stock. Returns shares sold."""
        position = self.get_portfolio_position(user_id, ticker)
        if position["shares"] < shares:
            shares = position["shares"]
        
        new_shares = position["shares"] - shares
        
        if new_shares == 0:
            self.cursor.execute(
                'DELETE FROM stock_portfolio WHERE user_id = ? AND ticker = ?',
                (user_id, ticker)
            )
        else:
            self.cursor.execute(
                'UPDATE stock_portfolio SET shares = ? WHERE user_id = ? AND ticker = ?',
                (new_shares, user_id, ticker)
            )
        self.connection.commit()
        return shares
    
    def get_total_shares_held(self, ticker: str) -> int:
        """Get total shares of a stock held by all users."""
        self.cursor.execute(
            'SELECT COALESCE(SUM(shares), 0) FROM stock_portfolio WHERE ticker = ?',
            (ticker,)
        )
        result = self.cursor.fetchone()
        return result[0] if result else 0
    
    def get_stock_price_history(self, ticker: str, hours: int = 24) -> list:
        """Get recent price history for momentum calculation."""
        cutoff = datetime.now(timezone.utc).timestamp() - (hours * 3600)
        self.cursor.execute(
            'SELECT price, timestamp FROM stock_history WHERE ticker = ? AND timestamp > ? ORDER BY timestamp DESC',
            (ticker, cutoff)
        )
        rows = self.cursor.fetchall()
        return [{"price": row[0], "timestamp": row[1]} for row in rows]
    
    def add_market_news(self, headline: str, affected_ticker: str = None, effect: str = None):
        """Add a market news item."""
        now = datetime.now(timezone.utc).timestamp()
        self.cursor.execute(
            'INSERT INTO market_news (headline, affected_ticker, effect, timestamp) VALUES (?, ?, ?, ?)',
            (headline, affected_ticker, effect, now)
        )
        self.connection.commit()
    
    def get_recent_news(self, limit: int = 5):
        """Get recent market news."""
        self.cursor.execute(
            'SELECT id, headline, affected_ticker, effect, timestamp FROM market_news ORDER BY timestamp DESC LIMIT ?',
            (limit,)
        )
        rows = self.cursor.fetchall()
        return [{
            "id": row[0],
            "headline": row[1],
            "affected_ticker": row[2],
            "effect": row[3],
            "timestamp": row[4]
        } for row in rows]
    
    def record_work_activity(self):
        """Record a work command being used (for stock market)."""
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        self.cursor.execute('''
            INSERT INTO work_activity (date, work_count) VALUES (?, 1)
            ON CONFLICT(date) DO UPDATE SET work_count = work_count + 1
        ''', (today,))
        self.connection.commit()
    
    def get_work_activity(self, days: int = 7):
        """Get work activity for the last N days."""
        self.cursor.execute(
            'SELECT date, work_count FROM work_activity ORDER BY date DESC LIMIT ?',
            (days,)
        )
        rows = self.cursor.fetchall()
        return {row[0]: row[1] for row in rows}
    
    # ========== MARKET CHANNELS ==========
    def set_market_channel(self, guild_id: str, channel_id: int):
        """Set the market announcement channel for a guild."""
        self.cursor.execute(
            'INSERT OR REPLACE INTO market_channels (guild_id, channel_id) VALUES (?, ?)',
            (guild_id, channel_id)
        )
        self.connection.commit()
    
    def get_market_channel(self, guild_id: str) -> int | None:
        """Get the market announcement channel for a guild."""
        self.cursor.execute(
            'SELECT channel_id FROM market_channels WHERE guild_id = ?',
            (guild_id,)
        )
        row = self.cursor.fetchone()
        return row[0] if row else None

db = Database()