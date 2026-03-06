"""Tests for cognitive_layers.py — Traits, Mood, Appraisal, Processing."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import PADVector, RelationshipVector, Stimulus
from sonarr.brain.blackboard import Blackboard
from sonarr.brain.emotion_engine import EmotionEngine
from sonarr.brain.cognitive_layers import TraitProfile, MoodTracker, AppraisalLayer, CognitiveProcessor


class TestTraitProfile:
    def test_neutral_traits_passthrough(self):
        traits = TraitProfile()
        impulse = PADVector(0.5, 0.3, 0.2)
        result = traits.apply(impulse)
        assert abs(result.pleasure - 0.5) < 1e-9
        assert abs(result.arousal - 0.3) < 1e-9

    def test_amplified_arousal(self):
        traits = TraitProfile(arousal_mod=2.0)
        impulse = PADVector(0.3, 0.3, 0.3)
        result = traits.apply(impulse)
        assert abs(result.arousal - 0.6) < 1e-9

    def test_dampened_pleasure(self):
        traits = TraitProfile(pleasure_mod=0.5)
        impulse = PADVector(0.8, 0.0, 0.0)
        result = traits.apply(impulse)
        assert abs(result.pleasure - 0.4) < 1e-9


class TestMoodTracker:
    def test_starts_neutral(self):
        mt = MoodTracker()
        m = mt.get_mood()
        assert m.pleasure == 0.0

    def test_update_moves_toward_emotion(self):
        mt = MoodTracker(alpha=0.5)
        mt.update(PADVector(1.0, 0.0, 0.0))
        m = mt.get_mood()
        assert m.pleasure > 0.0

    def test_update_converges(self):
        mt = MoodTracker(alpha=0.5)
        for _ in range(100):
            mt.update(PADVector(0.8, 0.0, 0.0))
        m = mt.get_mood()
        assert abs(m.pleasure - 0.8) < 0.05

    def test_decay(self):
        mt = MoodTracker()
        mt.mood = PADVector(0.8, 0.8, 0.8)
        mt.decay(50.0)
        m = mt.get_mood()
        assert m.pleasure < 0.5


class TestAppraisalLayer:
    def test_unknown_entity_passthrough(self):
        bb = Blackboard()
        impulse = PADVector(0.5, 0.3, 0.2)
        result, ctx = AppraisalLayer.appraise(impulse, "unknown", bb)
        assert abs(result.pleasure - 0.5) < 1e-9
        assert ctx["known"] == 0.0

    def test_trust_amplifies_pleasure(self):
        bb = Blackboard()
        bb.register_entity("friend", RelationshipVector(trust=1.0))
        impulse = PADVector(0.5, 0.0, 0.0)
        result, ctx = AppraisalLayer.appraise(impulse, "friend", bb)
        # trust=1.0 → pleasure_scale = 1.5
        assert result.pleasure > impulse.pleasure

    def test_familiarity_dampens(self):
        bb = Blackboard()
        bb.register_entity("old_friend", RelationshipVector(familiarity=1.0))
        impulse = PADVector(0.5, 0.5, 0.5)
        result, ctx = AppraisalLayer.appraise(impulse, "old_friend", bb)
        # High familiarity should dampen all axes
        assert result.pleasure < impulse.pleasure
        assert result.arousal < impulse.arousal

    def test_context_contains_scales(self):
        bb = Blackboard()
        bb.register_entity("e1", RelationshipVector(trust=0.5, fear=0.3))
        _, ctx = AppraisalLayer.appraise(PADVector(0.5, 0.5, 0.5), "e1", bb)
        assert "pleasure_scale" in ctx
        assert "arousal_scale" in ctx
        assert "dominance_scale" in ctx


class TestCognitiveProcessor:
    def test_full_pipeline_returns_cognitive_state(self):
        bb = Blackboard()
        bb.register_entity("alice", RelationshipVector(trust=0.5))
        ee = EmotionEngine()
        cp = CognitiveProcessor()
        stim = Stimulus("alice", "greeting", 0.7)
        state = cp.process_stimulus(stim, bb, ee)
        assert state.source_entity_id == "alice"
        assert state.emotion.pleasure != 0.0  # greeting should shift pleasure

    def test_different_entities_different_results(self):
        bb = Blackboard()
        bb.register_entity("friend", RelationshipVector(trust=0.9))
        bb.register_entity("enemy", RelationshipVector(trust=-0.8, fear=0.7))

        ee1 = EmotionEngine()
        ee2 = EmotionEngine()
        cp1 = CognitiveProcessor()
        cp2 = CognitiveProcessor()

        stim_friend = Stimulus("friend", "insult", 0.8)
        stim_enemy = Stimulus("enemy", "insult", 0.8)

        state1 = cp1.process_stimulus(stim_friend, bb, ee1)
        state2 = cp2.process_stimulus(stim_enemy, bb, ee2)

        # Same event but different entities → different emotional impact
        assert state1.emotion.pleasure != state2.emotion.pleasure

    def test_records_interaction_on_blackboard(self):
        bb = Blackboard()
        bb.register_entity("alice")
        ee = EmotionEngine()
        cp = CognitiveProcessor()
        stim = Stimulus("alice", "greeting", 0.5)
        cp.process_stimulus(stim, bb, ee)
        rec = bb.get_entity("alice")
        assert rec is not None
        assert len(rec.interaction_history) == 1
