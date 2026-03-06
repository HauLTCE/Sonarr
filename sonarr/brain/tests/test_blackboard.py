"""Tests for blackboard.py — Blackboard memory system."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import RelationshipVector, Stimulus
from sonarr.brain.blackboard import Blackboard


class TestBlackboard:
    def _make_bb(self) -> Blackboard:
        return Blackboard()

    def test_register_and_get_entity(self):
        bb = self._make_bb()
        rec = bb.register_entity("alice")
        assert rec.entity_id == "alice"
        assert bb.get_entity("alice") is rec

    def test_register_duplicate_returns_existing(self):
        bb = self._make_bb()
        r1 = bb.register_entity("alice")
        r2 = bb.register_entity("alice")
        assert r1 is r2

    def test_get_unknown_entity_returns_none(self):
        bb = self._make_bb()
        assert bb.get_entity("unknown") is None

    def test_get_or_create_entity(self):
        bb = self._make_bb()
        rec = bb.get_or_create_entity("bob")
        assert rec.entity_id == "bob"
        assert bb.get_entity("bob") is rec

    def test_register_with_relationship(self):
        bb = self._make_bb()
        rel = RelationshipVector(trust=0.8, fear=-0.3)
        rec = bb.register_entity("alice", relationship=rel)
        assert rec.relationship.trust == 0.8
        assert rec.relationship.fear == -0.3

    def test_update_relationship(self):
        bb = self._make_bb()
        bb.register_entity("alice", relationship=RelationshipVector(trust=0.5))
        bb.update_relationship("alice", "trust", 0.3)
        rec = bb.get_entity("alice")
        assert rec is not None
        assert abs(rec.relationship.trust - 0.8) < 1e-9

    def test_update_relationship_clamped(self):
        bb = self._make_bb()
        bb.register_entity("alice", relationship=RelationshipVector(trust=0.9))
        bb.update_relationship("alice", "trust", 0.5)
        rec = bb.get_entity("alice")
        assert rec is not None
        assert rec.relationship.trust == 1.0

    def test_record_interaction(self):
        bb = self._make_bb()
        bb.register_entity("alice")
        stim = Stimulus("alice", "greeting", 0.5)
        bb.record_interaction("alice", stim)
        rec = bb.get_entity("alice")
        assert rec is not None
        assert len(rec.interaction_history) == 1
        assert rec.interaction_history[0] is stim

    def test_interaction_history_ring_buffer(self):
        bb = self._make_bb()
        bb.register_entity("alice")
        for i in range(60):
            bb.record_interaction("alice", Stimulus("alice", "event", 0.5))
        rec = bb.get_entity("alice")
        assert rec is not None
        assert len(rec.interaction_history) == 50  # maxlen=50

    def test_global_state(self):
        bb = self._make_bb()
        bb.set_global("time_of_day", "morning")
        assert bb.get_global("time_of_day") == "morning"
        assert bb.get_global("missing", "default") == "default"

    def test_entity_ids(self):
        bb = self._make_bb()
        bb.register_entity("alice")
        bb.register_entity("bob")
        assert set(bb.entity_ids) == {"alice", "bob"}

    def test_snapshot(self):
        bb = self._make_bb()
        bb.register_entity("alice")
        bb.set_global("key", "value")
        snap = bb.snapshot()
        assert "alice" in snap["entities"]
        assert snap["global_state"]["key"] == "value"
