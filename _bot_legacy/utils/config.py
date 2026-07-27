import json
import os
import logging

logger = logging.getLogger("bot")

CONFIG_FILE = "server_config.json"


def load_config():
    """Load server configuration from DB into a dict (backwards-compatible)."""
    from utils.database import db
    config = {}
    try:
        db.cursor.execute("SELECT guild_id, config_key, config_value FROM guild_config")
        for row in db.cursor.fetchall():
            gid = row[0]
            key = row[1]
            val = row[2]
            if gid not in config:
                config[gid] = {}
            # Try to parse as int (channel IDs were stored as ints)
            try:
                config[gid][key] = int(val)
            except (ValueError, TypeError):
                config[gid][key] = val
    except Exception as e:
        logger.warning(f"[Config] Failed to load from DB, falling back to JSON: {e}")
        if os.path.exists(CONFIG_FILE):
            with open(CONFIG_FILE, "r") as f:
                return json.load(f)
    return config


def save_config(config):
    """Save server configuration to DB."""
    from utils.database import db
    try:
        for guild_id, guild_config in config.items():
            for key, value in guild_config.items():
                db.cursor.execute(
                    "INSERT OR REPLACE INTO guild_config (guild_id, config_key, config_value) VALUES (?, ?, ?)",
                    (str(guild_id), key, str(value))
                )
        db.connection.commit()
    except Exception as e:
        logger.error(f"[Config] Failed to save to DB: {e}")


def get_guild_config(guild_id, key, default=None):
    """Get a single config value for a guild from DB."""
    from utils.database import db
    try:
        db.cursor.execute(
            "SELECT config_value FROM guild_config WHERE guild_id = ? AND config_key = ?",
            (str(guild_id), key)
        )
        row = db.cursor.fetchone()
        if row:
            try:
                return int(row[0])
            except (ValueError, TypeError):
                return row[0]
    except Exception:
        pass
    return default


def set_guild_config(guild_id, key, value):
    """Set a single config value for a guild in DB."""
    from utils.database import db
    db.cursor.execute(
        "INSERT OR REPLACE INTO guild_config (guild_id, config_key, config_value) VALUES (?, ?, ?)",
        (str(guild_id), key, str(value))
    )
    db.connection.commit()


def get_channel_config(guild_id, config_dict, channel_type):
    """Get a specific channel config for a guild (backwards-compatible dict version)."""
    guild_id_str = str(guild_id)
    if guild_id_str not in config_dict:
        return None
    
    config = config_dict[guild_id_str]
    return config.get(channel_type)
