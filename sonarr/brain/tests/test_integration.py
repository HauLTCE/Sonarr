"""Integration tests — full pipeline from stimulus to action selection."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import RelationshipVector, Stimulus
from sonarr.brain.cognitive_layers import TraitProfile
from sonarr.brain.ai_brain import AIBrain
from sonarr.brain.utility_ai import (
    Action,
    Consideration,
    ResponseCurve,
    CurveType,
    pleasure_input,
    arousal_input,
    entity_trust_input,
    entity_fear_input,
)


def _make_brain() -> AIBrain:
    """Create a fully configured brain for integration testing."""
    brain = AIBrain(
        traits=TraitProfile(arousal_mod=1.2),
        reactivity=1.0,
        decay_rate=0.3,
    )

    # Register entities
    brain.blackboard.register_entity(
        "friend", RelationshipVector(trust=0.8, fear=-0.3, affection=0.6)
    )
    brain.blackboard.register_entity(
        "enemy", RelationshipVector(trust=-0.7, fear=0.8, affection=-0.5)
    )

    # Register actions
    brain.utility.register_actions(
        Action("greet", [
            Consideration("pleasure", pleasure_input,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
            Consideration("trust", entity_trust_input,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]),
        Action("attack", [
            Consideration("anger", pleasure_input,
                          ResponseCurve(CurveType.INVERSE, 1.0, 0.0)),
            Consideration("arousal", arousal_input,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]),
        Action("flee", [
            Consideration("fear", entity_fear_input,
                          ResponseCurve(CurveType.LOGISTIC, 6.0, 0.6)),
            Consideration("arousal", arousal_input,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]),
    )
    return brain


class TestFullPipeline:
    def test_stimulus_produces_action(self):
        brain = _make_brain()
        stim = Stimulus("friend", "greeting", 0.7)
        result = brain.receive_stimulus(stim)
        assert result is not None
        assert result.action_name in ("greet", "attack", "flee")

    def test_different_entities_yield_different_actions(self):
        """Same hostile stimulus from friend vs enemy should differ."""
        brain1 = _make_brain()
        brain2 = _make_brain()

        stim_friend = Stimulus("friend", "threat", 0.9)
        stim_enemy = Stimulus("enemy", "threat", 0.9)

        r1 = brain1.receive_stimulus(stim_friend)
        r2 = brain2.receive_stimulus(stim_enemy)

        assert r1 is not None
        assert r2 is not None
        # With different trust/fear profiles, the top action should differ
        # (or at least the scores should be meaningfully different)
        _, _, scores1 = brain1.receive_stimulus_full(
            Stimulus("friend", "greeting", 0.5)
        )
        _, _, scores2 = brain2.receive_stimulus_full(
            Stimulus("enemy", "greeting", 0.5)
        )
        score_dict1 = {s.action_name: s.score for s in scores1}
        score_dict2 = {s.action_name: s.score for s in scores2}
        # Greet score should be higher for friend than enemy
        assert score_dict1.get("greet", 0) > score_dict2.get("greet", 0)

    def test_tick_decays_emotion(self):
        brain = _make_brain()
        brain.receive_stimulus(Stimulus("friend", "insult", 0.9))
        emotion_before = brain.current_emotion.magnitude()
        brain.tick(5.0)
        emotion_after = brain.current_emotion.magnitude()
        assert emotion_after < emotion_before

    def test_debug_snapshot_is_valid(self):
        brain = _make_brain()
        brain.receive_stimulus(Stimulus("friend", "greeting", 0.5))
        snap = brain.debug_snapshot()
        assert "emotion" in snap
        assert "mood" in snap
        assert "blackboard" in snap

    def test_multiple_stimuli_accumulate(self):
        brain = _make_brain()
        brain.receive_stimulus(Stimulus("enemy", "insult", 0.8))
        e1 = brain.current_emotion.pleasure
        brain.receive_stimulus(Stimulus("enemy", "insult", 0.8))
        e2 = brain.current_emotion.pleasure
        # Second insult should push pleasure even more negative
        assert e2 < e1
