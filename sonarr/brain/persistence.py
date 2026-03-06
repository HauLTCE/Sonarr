"""
persistence.py — SQLite persistence for the AI brain state.

Saves and loads:
  - Per-entity relationship vectors (trust, fear, respect, etc.)
  - Bot emotional state (PAD vector + mood) per guild
  - Entity interaction metadata

Tables are created automatically on first use.
"""

from __future__ import annotations

import logging
import sqlite3
from pathlib import Path
from datetime import datetime, timezone

from .core_types import PADVector, RelationshipVector
from .blackboard import Blackboard, EntityRecord

logger = logging.getLogger("bot")

DB_PATH = Path("bot_data.db")


class BrainPersistence:
    """Save/load AI brain state to/from SQLite."""

    def __init__(self, db_path: Path | None = None):
        self.db_path = db_path or DB_PATH
        self._ensure_tables()

    def _get_conn(self) -> sqlite3.Connection:
        conn = sqlite3.connect(str(self.db_path), check_same_thread=False, timeout=10.0)
        conn.row_factory = sqlite3.Row
        return conn

    def _ensure_tables(self) -> None:
        """Create brain-specific tables if they don't exist."""
        conn = self._get_conn()
        try:
            conn.execute('''
                CREATE TABLE IF NOT EXISTS entity_state (
                    entity_id TEXT NOT NULL,
                    guild_id TEXT NOT NULL,
                    trust REAL DEFAULT 0.0,
                    fear REAL DEFAULT 0.0,
                    respect REAL DEFAULT 0.0,
                    familiarity REAL DEFAULT 0.0,
                    affection REAL DEFAULT 0.0,
                    interaction_count INTEGER DEFAULT 0,
                    last_interaction REAL,
                    PRIMARY KEY (entity_id, guild_id)
                )
            ''')
            conn.execute('''
                CREATE TABLE IF NOT EXISTS bot_emotional_state (
                    guild_id TEXT PRIMARY KEY,
                    pleasure REAL DEFAULT 0.0,
                    arousal REAL DEFAULT 0.0,
                    dominance REAL DEFAULT 0.0,
                    mood_pleasure REAL DEFAULT 0.0,
                    mood_arousal REAL DEFAULT 0.0,
                    mood_dominance REAL DEFAULT 0.0,
                    updated_at REAL
                )
            ''')
            conn.commit()
            logger.info("[Brain] Persistence tables ready")
        finally:
            conn.close()

    # === SAVE ===

    def save_entity(self, entity_id: str, guild_id: str,
                    rel: RelationshipVector, interaction_count: int = 0) -> None:
        """Save or update a single entity's relationship state."""
        now = datetime.now(timezone.utc).timestamp()
        conn = self._get_conn()
        try:
            conn.execute('''
                INSERT OR REPLACE INTO entity_state
                (entity_id, guild_id, trust, fear, respect, familiarity,
                 affection, interaction_count, last_interaction)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                entity_id, guild_id,
                rel.trust, rel.fear, rel.respect,
                rel.familiarity, rel.affection,
                interaction_count, now,
            ))
            conn.commit()
        finally:
            conn.close()

    def save_emotional_state(self, guild_id: str,
                             emotion: PADVector, mood: PADVector) -> None:
        """Save the bot's current emotional state for a guild."""
        now = datetime.now(timezone.utc).timestamp()
        conn = self._get_conn()
        try:
            conn.execute('''
                INSERT OR REPLACE INTO bot_emotional_state
                (guild_id, pleasure, arousal, dominance,
                 mood_pleasure, mood_arousal, mood_dominance, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                guild_id,
                emotion.pleasure, emotion.arousal, emotion.dominance,
                mood.pleasure, mood.arousal, mood.dominance,
                now,
            ))
            conn.commit()
        finally:
            conn.close()

    def save_blackboard(self, blackboard: Blackboard, guild_id: str) -> None:
        """Save all entities from the blackboard for a guild."""
        conn = self._get_conn()
        now = datetime.now(timezone.utc).timestamp()
        try:
            for entity_id in blackboard.entity_ids:
                record = blackboard.get_entity(entity_id)
                if record is None:
                    continue
                rel = record.relationship
                conn.execute('''
                    INSERT OR REPLACE INTO entity_state
                    (entity_id, guild_id, trust, fear, respect, familiarity,
                     affection, interaction_count, last_interaction)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (
                    entity_id, guild_id,
                    rel.trust, rel.fear, rel.respect,
                    rel.familiarity, rel.affection,
                    len(record.interaction_history), now,
                ))
            conn.commit()
            logger.debug(f"[Brain] Saved {len(blackboard.entity_ids)} entities for guild {guild_id}")
        finally:
            conn.close()

    # === LOAD ===

    def load_entities(self, blackboard: Blackboard, guild_id: str) -> int:
        """Load all entity relationships for a guild into the blackboard.

        Returns the number of entities loaded.
        """
        conn = self._get_conn()
        try:
            cursor = conn.execute(
                'SELECT * FROM entity_state WHERE guild_id = ?', (guild_id,)
            )
            count = 0
            for row in cursor:
                rel = RelationshipVector(
                    trust=row["trust"],
                    fear=row["fear"],
                    respect=row["respect"],
                    familiarity=row["familiarity"],
                    affection=row["affection"],
                )
                blackboard.register_entity(
                    entity_id=row["entity_id"],
                    relationship=rel,
                    metadata={
                        "interaction_count": row["interaction_count"],
                        "last_interaction": row["last_interaction"],
                    },
                )
                count += 1
            logger.info(f"[Brain] Loaded {count} entities for guild {guild_id}")
            return count
        finally:
            conn.close()

    def load_emotional_state(self, guild_id: str) -> tuple[PADVector, PADVector] | None:
        """Load the bot's emotional state for a guild.

        Returns (emotion, mood) or None if no saved state.
        """
        conn = self._get_conn()
        try:
            cursor = conn.execute(
                'SELECT * FROM bot_emotional_state WHERE guild_id = ?', (guild_id,)
            )
            row = cursor.fetchone()
            if row is None:
                return None

            emotion = PADVector(
                pleasure=row["pleasure"],
                arousal=row["arousal"],
                dominance=row["dominance"],
            )
            mood = PADVector(
                pleasure=row["mood_pleasure"],
                arousal=row["mood_arousal"],
                dominance=row["mood_dominance"],
            )
            logger.info(f"[Brain] Loaded emotional state for guild {guild_id}")
            return emotion, mood
        finally:
            conn.close()

    def get_entity_relationship(self, entity_id: str, guild_id: str) -> RelationshipVector | None:
        """Get a single entity's relationship vector."""
        conn = self._get_conn()
        try:
            cursor = conn.execute(
                'SELECT * FROM entity_state WHERE entity_id = ? AND guild_id = ?',
                (entity_id, guild_id),
            )
            row = cursor.fetchone()
            if row is None:
                return None
            return RelationshipVector(
                trust=row["trust"],
                fear=row["fear"],
                respect=row["respect"],
                familiarity=row["familiarity"],
                affection=row["affection"],
            )
        finally:
            conn.close()
