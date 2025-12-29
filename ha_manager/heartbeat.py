"""
Heartbeat Service for High Availability.

Maintains a heartbeat in the database to indicate this instance is alive.
Other instances monitor this heartbeat to detect failures.
"""

import asyncio
import logging
from datetime import datetime, timezone
from typing import Optional, Callable, Awaitable

logger = logging.getLogger("bot.ha")


class HeartbeatService:
    """
    Manages heartbeat signals for leader election.
    
    The leader instance sends regular heartbeat updates to the database.
    Standby instances monitor the heartbeat to detect leader failures.
    """
    
    def __init__(
        self,
        instance_id: str,
        instance_name: str,
        heartbeat_interval: int = 10,
        leader_timeout: int = 30,
        db_pool = None
    ):
        self.instance_id = instance_id
        self.instance_name = instance_name
        self.heartbeat_interval = heartbeat_interval
        self.leader_timeout = leader_timeout
        self.db_pool = db_pool
        
        self._running = False
        self._task: Optional[asyncio.Task] = None
        self._is_leader = False
        
        # Callbacks
        self._on_become_leader: Optional[Callable[[], Awaitable[None]]] = None
        self._on_lose_leadership: Optional[Callable[[], Awaitable[None]]] = None
        self._on_leader_changed: Optional[Callable[[str], Awaitable[None]]] = None
    
    def on_become_leader(self, callback: Callable[[], Awaitable[None]]):
        """Register callback for when this instance becomes leader."""
        self._on_become_leader = callback
    
    def on_lose_leadership(self, callback: Callable[[], Awaitable[None]]):
        """Register callback for when this instance loses leadership."""
        self._on_lose_leadership = callback
    
    def on_leader_changed(self, callback: Callable[[str], Awaitable[None]]):
        """Register callback for when leader changes (receives new leader ID)."""
        self._on_leader_changed = callback
    
    @property
    def is_leader(self) -> bool:
        """Check if this instance is currently the leader."""
        return self._is_leader
    
    async def start(self):
        """Start the heartbeat service."""
        if self._running:
            return
        
        self._running = True
        self._task = asyncio.create_task(self._heartbeat_loop())
        logger.info(f"[HA] Heartbeat service started for {self.instance_name}")
    
    async def stop(self):
        """Stop the heartbeat service."""
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        
        # Release leadership if we were leader
        if self._is_leader:
            await self._release_leadership()
        
        logger.info(f"[HA] Heartbeat service stopped for {self.instance_name}")
    
    async def _heartbeat_loop(self):
        """Main heartbeat loop."""
        while self._running:
            try:
                if self._is_leader:
                    # Update heartbeat as leader
                    await self._update_heartbeat()
                else:
                    # Check if we should become leader
                    await self._check_leadership()
                
                await asyncio.sleep(self.heartbeat_interval)
            
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"[HA] Heartbeat error: {e}")
                await asyncio.sleep(5)  # Brief pause on error
    
    async def _update_heartbeat(self):
        """Update our heartbeat in the database."""
        if not self.db_pool:
            return
        
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.db_pool.acquire() as conn:
            # Update our heartbeat
            await conn.execute('''
                UPDATE ha_leader 
                SET last_heartbeat = $1, instance_name = $2
                WHERE instance_id = $3
            ''', now, self.instance_name, self.instance_id)
            
            # Check if we're still the leader
            row = await conn.fetchrow('''
                SELECT instance_id FROM ha_leader WHERE id = 1
            ''')
            
            if row and row['instance_id'] != self.instance_id:
                # We lost leadership
                logger.warning(f"[HA] Lost leadership to {row['instance_id']}")
                self._is_leader = False
                if self._on_lose_leadership:
                    await self._on_lose_leadership()
    
    async def _check_leadership(self):
        """Check if we should become leader."""
        if not self.db_pool:
            # No HA database, assume we're leader
            if not self._is_leader:
                self._is_leader = True
                logger.info("[HA] No HA database configured, assuming leadership")
                if self._on_become_leader:
                    await self._on_become_leader()
            return
        
        now = datetime.now(timezone.utc).timestamp()
        timeout_threshold = now - self.leader_timeout
        
        async with self.db_pool.acquire() as conn:
            # Try to claim leadership if no leader or leader timed out
            result = await conn.execute('''
                INSERT INTO ha_leader (id, instance_id, instance_name, last_heartbeat, elected_at)
                VALUES (1, $1, $2, $3, $3)
                ON CONFLICT (id) DO UPDATE
                SET instance_id = $1, instance_name = $2, last_heartbeat = $3, elected_at = $3
                WHERE ha_leader.last_heartbeat < $4
            ''', self.instance_id, self.instance_name, now, timeout_threshold)
            
            # Check result
            if result == 'INSERT 0 1' or 'UPDATE 1' in result:
                # We became leader
                if not self._is_leader:
                    self._is_leader = True
                    logger.info(f"[HA] {self.instance_name} became leader!")
                    if self._on_become_leader:
                        await self._on_become_leader()
            else:
                # Someone else is leader
                row = await conn.fetchrow('''
                    SELECT instance_id, instance_name FROM ha_leader WHERE id = 1
                ''')
                if row and self._on_leader_changed:
                    await self._on_leader_changed(row['instance_id'])
    
    async def _release_leadership(self):
        """Voluntarily release leadership."""
        if not self.db_pool or not self._is_leader:
            return
        
        async with self.db_pool.acquire() as conn:
            # Set heartbeat to past so others can claim immediately
            await conn.execute('''
                UPDATE ha_leader 
                SET last_heartbeat = 0
                WHERE instance_id = $1
            ''', self.instance_id)
        
        self._is_leader = False
        logger.info(f"[HA] {self.instance_name} released leadership")
    
    async def force_become_leader(self):
        """Force this instance to become leader (for manual intervention)."""
        if not self.db_pool:
            self._is_leader = True
            return
        
        now = datetime.now(timezone.utc).timestamp()
        
        async with self.db_pool.acquire() as conn:
            await conn.execute('''
                INSERT INTO ha_leader (id, instance_id, instance_name, last_heartbeat, elected_at)
                VALUES (1, $1, $2, $3, $3)
                ON CONFLICT (id) DO UPDATE
                SET instance_id = $1, instance_name = $2, last_heartbeat = $3, elected_at = $3
            ''', self.instance_id, self.instance_name, now)
        
        self._is_leader = True
        logger.info(f"[HA] {self.instance_name} forced leadership takeover")
        if self._on_become_leader:
            await self._on_become_leader()
    
    async def get_current_leader(self) -> Optional[dict]:
        """Get information about the current leader."""
        if not self.db_pool:
            if self._is_leader:
                return {
                    'instance_id': self.instance_id,
                    'instance_name': self.instance_name,
                    'is_self': True
                }
            return None
        
        async with self.db_pool.acquire() as conn:
            row = await conn.fetchrow('''
                SELECT instance_id, instance_name, last_heartbeat, elected_at
                FROM ha_leader WHERE id = 1
            ''')
            
            if not row:
                return None
            
            return {
                'instance_id': row['instance_id'],
                'instance_name': row['instance_name'],
                'last_heartbeat': row['last_heartbeat'],
                'elected_at': row['elected_at'],
                'is_self': row['instance_id'] == self.instance_id
            }
    
    async def get_all_instances(self) -> list:
        """Get all registered instances (requires ha_instances table)."""
        if not self.db_pool:
            return [{
                'instance_id': self.instance_id,
                'instance_name': self.instance_name,
                'is_leader': self._is_leader,
                'is_self': True
            }]
        
        async with self.db_pool.acquire() as conn:
            rows = await conn.fetch('''
                SELECT instance_id, instance_name, last_seen, status
                FROM ha_instances
                ORDER BY last_seen DESC
            ''')
            
            leader = await self.get_current_leader()
            leader_id = leader['instance_id'] if leader else None
            
            return [{
                'instance_id': row['instance_id'],
                'instance_name': row['instance_name'],
                'last_seen': row['last_seen'],
                'status': row['status'],
                'is_leader': row['instance_id'] == leader_id,
                'is_self': row['instance_id'] == self.instance_id
            } for row in rows]
