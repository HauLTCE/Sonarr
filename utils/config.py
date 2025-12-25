import json
import os
import logging

logger = logging.getLogger("bot")

CONFIG_FILE = "server_config.json"

def load_config():
    """Load server configuration from file."""
    if os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE, "r") as f:
            return json.load(f)
    return {}

def save_config(config):
    """Save server configuration to file."""
    with open(CONFIG_FILE, "w") as f:
        json.dump(config, f)

def get_channel_config(guild_id, config_dict, channel_type):
    """Get a specific channel config for a guild."""
    guild_id_str = str(guild_id)
    if guild_id_str not in config_dict:
        return None
    
    config = config_dict[guild_id_str]
    return config.get(channel_type)
