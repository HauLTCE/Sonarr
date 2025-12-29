"""
HA Configuration for distributed bot instances.
"""

import os
import uuid
import socket
from dataclasses import dataclass
from typing import Optional


@dataclass
class HAConfig:
    """Configuration for High Availability setup."""
    
    # Instance identification
    instance_id: str = None  # Unique ID for this instance
    instance_name: str = None  # Human-readable name
    
    # Leader election settings
    heartbeat_interval: int = 10  # Seconds between heartbeats
    leader_timeout: int = 30  # Seconds before considering leader dead
    election_delay: int = 5  # Seconds to wait before claiming leadership
    
    # Database connection
    database_url: str = None  # PostgreSQL connection string
    
    # Mode
    enabled: bool = True  # Whether HA is enabled
    
    def __post_init__(self):
        """Initialize default values."""
        if self.instance_id is None:
            self.instance_id = str(uuid.uuid4())[:8]
        
        if self.instance_name is None:
            hostname = socket.gethostname()
            self.instance_name = f"sonarr-{hostname}-{self.instance_id}"
    
    @classmethod
    def from_env(cls) -> 'HAConfig':
        """Create configuration from environment variables."""
        return cls(
            instance_id=os.getenv('HA_INSTANCE_ID'),
            instance_name=os.getenv('HA_INSTANCE_NAME'),
            heartbeat_interval=int(os.getenv('HA_HEARTBEAT_INTERVAL', 10)),
            leader_timeout=int(os.getenv('HA_LEADER_TIMEOUT', 30)),
            election_delay=int(os.getenv('HA_ELECTION_DELAY', 5)),
            database_url=os.getenv('DATABASE_URL'),
            enabled=os.getenv('HA_ENABLED', 'true').lower() == 'true'
        )


# Default singleton instance
_config: Optional[HAConfig] = None


def get_config() -> HAConfig:
    """Get or create the HA configuration singleton."""
    global _config
    if _config is None:
        _config = HAConfig.from_env()
    return _config


def set_config(config: HAConfig):
    """Set the HA configuration singleton."""
    global _config
    _config = config
