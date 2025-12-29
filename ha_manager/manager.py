"""
High Availability Manager

Main coordinator for leader election and failover handling.
Manages the lifecycle of the bot based on leadership status.
"""

import asyncio
import logging
from typing import Optional, Callable, Awaitable
from datetime import datetime, timezone

from .config import HAConfig, get_config
from .heartbeat import HeartbeatService

logger = logging.getLogger("bot.ha")


class HAManager:
    """
    High Availability Manager for distributed bot instances.
    
    This manager coordinates:
    - Leader election via database heartbeat
    - Automatic failover when leader dies
    - Graceful handover when shutting down
    
    Usage:
        ha = HAManager(bot)
        await ha.start()
        
        # Bot will only be active when this instance is leader
        if ha.is_leader:
            # Do leader-only tasks
            pass
    """
    
    def __init__(
        self,
        bot = None,
        config: HAConfig = None,
        db_pool = None
    ):
        self.bot = bot
        self.config = config or get_config()
        self.db_pool = db_pool
        
        self._heartbeat = HeartbeatService(
            instance_id=self.config.instance_id,
            instance_name=self.config.instance_name,
            heartbeat_interval=self.config.heartbeat_interval,
            leader_timeout=self.config.leader_timeout,
            db_pool=db_pool
        )
        
        self._started = False
        self._bot_connected = False
        
        # Callbacks
        self._on_activated: Optional[Callable[[], Awaitable[None]]] = None
        self._on_deactivated: Optional[Callable[[], Awaitable[None]]] = None
        
        # Setup heartbeat callbacks
        self._heartbeat.on_become_leader(self._handle_become_leader)
        self._heartbeat.on_lose_leadership(self._handle_lose_leadership)
    
    @property
    def is_leader(self) -> bool:
        """Check if this instance is the active leader."""
        return self._heartbeat.is_leader
    
    @property
    def instance_id(self) -> str:
        """Get this instance's unique ID."""
        return self.config.instance_id
    
    @property
    def instance_name(self) -> str:
        """Get this instance's name."""
        return self.config.instance_name
    
    def on_activated(self, callback: Callable[[], Awaitable[None]]):
        """Register callback for when this instance becomes active."""
        self._on_activated = callback
    
    def on_deactivated(self, callback: Callable[[], Awaitable[None]]):
        """Register callback for when this instance becomes standby."""
        self._on_deactivated = callback
    
    async def start(self):
        """Start the HA manager."""
        if self._started:
            return
        
        if not self.config.enabled:
            logger.info("[HA] HA mode disabled, running as standalone")
            self._heartbeat._is_leader = True
            if self._on_activated:
                await self._on_activated()
            return
        
        # Initialize HA tables if needed
        await self._init_ha_tables()
        
        # Register this instance
        await self._register_instance()
        
        # Start heartbeat service
        await self._heartbeat.start()
        self._started = True
        
        logger.info(f"[HA] Manager started: {self.instance_name} ({self.instance_id})")
    
    async def stop(self):
        """Stop the HA manager gracefully."""
        if not self._started:
            return
        
        logger.info(f"[HA] Stopping manager: {self.instance_name}")
        
        # Stop heartbeat (releases leadership)
        await self._heartbeat.stop()
        
        # Update instance status
        await self._update_instance_status('offline')
        
        self._started = False
        logger.info(f"[HA] Manager stopped: {self.instance_name}")
    
    async def _init_ha_tables(self):
        """Initialize HA tables in the database."""
        if not self.db_pool:
            return
        
        async with self.db_pool.acquire() as conn:
            # Leader election table
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS ha_leader (
                    id INTEGER PRIMARY KEY DEFAULT 1,
                    instance_id TEXT NOT NULL,
                    instance_name TEXT NOT NULL,
                    last_heartbeat REAL NOT NULL,
                    elected_at REAL NOT NULL,
                    CONSTRAINT single_leader CHECK (id = 1)
                )
            ''')
            
            # Instance registry table
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS ha_instances (
                    instance_id TEXT PRIMARY KEY,
                    instance_name TEXT NOT NULL,
                    hostname TEXT,
                    ip_address TEXT,
                    last_seen REAL NOT NULL,
                    status TEXT DEFAULT 'online',
                    registered_at REAL NOT NULL
                )
            ''')
            
            # HA event log for debugging
            await conn.execute('''
                CREATE TABLE IF NOT EXISTS ha_events (
                    id SERIAL PRIMARY KEY,
                    instance_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    details TEXT,
                    timestamp REAL NOT NULL
                )
            ''')
        
        logger.info("[HA] HA tables initialized")
    
    async def _register_instance(self):
        """Register this instance in the database."""
        if not self.db_pool:
            return
        
        import socket
        now = datetime.now(timezone.utc).timestamp()
        hostname = socket.gethostname()
        
        # Try to get IP address
        try:
            ip_address = socket.gethostbyname(hostname)
        except:
            ip_address = 'unknown'
        
        async with self.db_pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO ha_instances (instance_id, instance_name, hostname, ip_address, last_seen, status, registered_at)
                VALUES ($1, $2, $3, $4, $5, 'online', $5)
                ON CONFLICT (instance_id) DO UPDATE
                SET instance_name = $2, hostname = $3, ip_address = $4, last_seen = $5, status = 'online'
            ''', self.instance_id, self.instance_name, hostname, ip_address, now)
            
            # Log registration event
            await conn.execute('''
                INSERT INTO ha_events (instance_id, event_type, details, timestamp)
                VALUES ($1, 'registered', $2, $3)
            ''', self.instance_id, f"Registered from {hostname} ({ip_address})", now)
        
        logger.info(f"[HA] Instance registered: {self.instance_name} @ {hostname}")
    
    async def _update_instance_status(self, status: str):
        """Update this instance's status in the registry."""
        if not self.db_pool:
            return
        
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.db_pool.acquire() as conn:
            await conn.execute('''
                UPDATE ha_instances
                SET status = $1, last_seen = $2
                WHERE instance_id = $3
            ''', status, now, self.instance_id)
            
            await conn.execute('''
                INSERT INTO ha_events (instance_id, event_type, details, timestamp)
                VALUES ($1, 'status_change', $2, $3)
            ''', self.instance_id, f"Status changed to {status}", now)
    
    async def _handle_become_leader(self):
        """Handle becoming the leader."""
        logger.info(f"[HA] {self.instance_name} is now the LEADER")
        
        await self._update_instance_status('leader')
        await self._log_event('became_leader', 'This instance is now the active leader')
        
        if self._on_activated:
            await self._on_activated()
        
        # Connect the bot if we have one
        if self.bot and not self._bot_connected:
            await self._connect_bot()
    
    async def _handle_lose_leadership(self):
        """Handle losing leadership."""
        logger.warning(f"[HA] {self.instance_name} LOST leadership")
        
        await self._update_instance_status('standby')
        await self._log_event('lost_leadership', 'This instance is now on standby')
        
        if self._on_deactivated:
            await self._on_deactivated()
        
        # Disconnect the bot if we have one
        if self.bot and self._bot_connected:
            await self._disconnect_bot()
    
    async def _connect_bot(self):
        """Connect the Discord bot."""
        if not self.bot:
            return
        
        logger.info("[HA] Connecting Discord bot...")
        # The actual connection is handled by main.py
        # This just signals that we should connect
        self._bot_connected = True
    
    async def _disconnect_bot(self):
        """Disconnect the Discord bot gracefully."""
        if not self.bot:
            return
        
        logger.info("[HA] Disconnecting Discord bot...")
        try:
            await self.bot.close()
        except Exception as e:
            logger.error(f"[HA] Error disconnecting bot: {e}")
        self._bot_connected = False
    
    async def _log_event(self, event_type: str, details: str):
        """Log an HA event to the database."""
        if not self.db_pool:
            return
        
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.db_pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO ha_events (instance_id, event_type, details, timestamp)
                VALUES ($1, $2, $3, $4)
            ''', self.instance_id, event_type, details, now)
    
    async def get_status(self) -> dict:
        """Get the current HA status."""
        leader = await self._heartbeat.get_current_leader()
        instances = await self._heartbeat.get_all_instances()
        
        return {
            'instance_id': self.instance_id,
            'instance_name': self.instance_name,
            'is_leader': self.is_leader,
            'status': 'leader' if self.is_leader else 'standby',
            'current_leader': leader,
            'all_instances': instances,
            'config': {
                'heartbeat_interval': self.config.heartbeat_interval,
                'leader_timeout': self.config.leader_timeout,
                'election_delay': self.config.election_delay
            }
        }
    
    async def force_failover(self):
        """Force a failover to this instance (admin command)."""
        logger.warning(f"[HA] Forcing failover to {self.instance_name}")
        await self._heartbeat.force_become_leader()
        await self._log_event('forced_failover', 'Admin forced this instance to become leader')
