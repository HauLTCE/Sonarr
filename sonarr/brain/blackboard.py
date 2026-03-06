"""
blackboard.py — Pillar 1: The Blackboard (Memory).

Central data repository keyed by Entity ID.  Stores relationship vectors,
interaction histories, and global state accessible to all other pillars.
"""

from __future__ import annotations

import threading
from collections import deque
from dataclasses import dataclass, field
from typing import Any

from .core_types import RelationshipVector, Stimulus


# ---------------------------------------------------------------------------
# Entity Record
# ---------------------------------------------------------------------------

@dataclass
class EntityRecord:
    """Everything the system knows about a single tracked entity."""

    entity_id: str
    relationship: RelationshipVector = field(default_factory=RelationshipVector)
    interaction_history: deque[Stimulus] = field(
        default_factory=lambda: deque(maxlen=50)
    )
    metadata: dict[str, Any] = field(default_factory=dict)

    def record_interaction(self, stimulus: Stimulus) -> None:
        """Append a stimulus to the capped history ring-buffer."""
        self.interaction_history.append(stimulus)

    def __repr__(self) -> str:
        return (
            f"Entity({self.entity_id!r}, {self.relationship}, "
            f"history_len={len(self.interaction_history)})"
        )


# ---------------------------------------------------------------------------
# Blackboard
# ---------------------------------------------------------------------------

class Blackboard:
    """Central shared memory for the AI system.

    * **Global state** — arbitrary key/value pairs visible to every pillar.
    * **Entity registry** — per-entity relationship vectors and histories.

    All mutations are guarded by a :class:`threading.Lock` so the Blackboard
    can safely be shared across threads in future async designs.
    """

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._global_state: dict[str, Any] = {}
        self._entities: dict[str, EntityRecord] = {}

    # --- global state --------------------------------------------------------

    def set_global(self, key: str, value: Any) -> None:
        with self._lock:
            self._global_state[key] = value

    def get_global(self, key: str, default: Any = None) -> Any:
        with self._lock:
            return self._global_state.get(key, default)

    @property
    def global_state(self) -> dict[str, Any]:
        """Read-only snapshot of global state."""
        with self._lock:
            return dict(self._global_state)

    # --- entity management ---------------------------------------------------

    def register_entity(
        self,
        entity_id: str,
        relationship: RelationshipVector | None = None,
        metadata: dict[str, Any] | None = None,
    ) -> EntityRecord:
        """Register a new entity (or return existing one)."""
        with self._lock:
            if entity_id in self._entities:
                return self._entities[entity_id]
            record = EntityRecord(
                entity_id=entity_id,
                relationship=relationship or RelationshipVector(),
                metadata=metadata or {},
            )
            self._entities[entity_id] = record
            return record

    def get_entity(self, entity_id: str) -> EntityRecord | None:
        """Retrieve an entity record, or *None* if not registered."""
        with self._lock:
            return self._entities.get(entity_id)

    def get_or_create_entity(self, entity_id: str) -> EntityRecord:
        """Get an existing entity or auto-register a new one."""
        with self._lock:
            if entity_id not in self._entities:
                self._entities[entity_id] = EntityRecord(entity_id=entity_id)
            return self._entities[entity_id]

    def update_relationship(
        self, entity_id: str, dimension: str, delta: float
    ) -> None:
        """Shift one relationship dimension for *entity_id* by *delta*."""
        record = self.get_or_create_entity(entity_id)
        with self._lock:
            record.relationship.update(dimension, delta)

    def record_interaction(self, entity_id: str, stimulus: Stimulus) -> None:
        """Append *stimulus* to the entity's interaction history."""
        record = self.get_or_create_entity(entity_id)
        with self._lock:
            record.record_interaction(stimulus)

    @property
    def entity_ids(self) -> list[str]:
        with self._lock:
            return list(self._entities.keys())

    # --- introspection -------------------------------------------------------

    def snapshot(self) -> dict[str, Any]:
        """Return a JSON-friendly snapshot of the entire Blackboard."""
        with self._lock:
            return {
                "global_state": dict(self._global_state),
                "entities": {
                    eid: {
                        "relationship": rec.relationship.as_dict(),
                        "history_length": len(rec.interaction_history),
                        "metadata": rec.metadata,
                    }
                    for eid, rec in self._entities.items()
                },
            }

    def __repr__(self) -> str:
        return (
            f"Blackboard(globals={len(self._global_state)}, "
            f"entities={list(self._entities.keys())})"
        )
