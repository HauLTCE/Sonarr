"""Tests for utility_ai.py — Response curves, considerations, action scoring."""

import sys
import os
# sys.path handled by pytest

from sonarr.brain.core_types import PADVector, CognitiveState, RelationshipVector
from sonarr.brain.blackboard import Blackboard
from sonarr.brain.utility_ai import (
    ResponseCurve,
    CurveType,
    Consideration,
    Action,
    UtilityAI,
    pleasure_input,
    arousal_input,
    entity_trust_input,
    emotional_intensity_input,
)


# ---------------------------------------------------------------------------
# Response Curve tests
# ---------------------------------------------------------------------------

class TestResponseCurve:
    def test_linear(self):
        curve = ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0)
        assert abs(curve.evaluate(0.5) - 0.5) < 1e-9
        assert curve.evaluate(2.0) == 1.0  # clamped
        assert curve.evaluate(-2.0) == 0.0  # clamped

    def test_linear_with_shift(self):
        curve = ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.2)
        assert abs(curve.evaluate(0.3) - 0.5) < 1e-9

    def test_quadratic(self):
        curve = ResponseCurve(CurveType.QUADRATIC, slope=1.0, shift=0.0, exponent=2.0)
        assert abs(curve.evaluate(0.5) - 0.25) < 1e-9
        assert abs(curve.evaluate(1.0) - 1.0) < 1e-9

    def test_logistic_midpoint(self):
        curve = ResponseCurve(CurveType.LOGISTIC, slope=10.0, shift=0.5)
        assert abs(curve.evaluate(0.5) - 0.5) < 1e-9

    def test_logistic_extremes(self):
        curve = ResponseCurve(CurveType.LOGISTIC, slope=10.0, shift=0.5)
        assert curve.evaluate(1.0) > 0.99
        assert curve.evaluate(0.0) < 0.01

    def test_step(self):
        curve = ResponseCurve(CurveType.STEP, shift=0.5)
        assert curve.evaluate(0.6) == 1.0
        assert curve.evaluate(0.4) == 0.0
        assert curve.evaluate(0.5) == 0.0  # not strictly greater

    def test_inverse(self):
        curve = ResponseCurve(CurveType.INVERSE, slope=1.0, shift=0.0)
        assert abs(curve.evaluate(0.0) - 1.0) < 1e-9
        assert abs(curve.evaluate(1.0) - 0.0) < 1e-9


# ---------------------------------------------------------------------------
# Consideration tests
# ---------------------------------------------------------------------------

class TestConsideration:
    def test_scores_through_curve(self):
        c = Consideration(
            "pleasure",
            pleasure_input,
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        state = CognitiveState(emotion=PADVector(0.5, 0.0, 0.0))
        bb = Blackboard()
        score = c.score(state, bb)
        # pleasure=0.5 → normalized to (0.5+1)/2 = 0.75
        assert abs(score - 0.75) < 1e-9


# ---------------------------------------------------------------------------
# Action tests
# ---------------------------------------------------------------------------

class TestAction:
    def test_multiplicative_scoring(self):
        c1 = Consideration(
            "a", lambda s, b: 0.8,
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        c2 = Consideration(
            "b", lambda s, b: 0.5,
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        action = Action("test", [c1, c2], weight=1.0)
        state = CognitiveState()
        bb = Blackboard()
        score = action.score(state, bb)
        assert abs(score - 0.8 * 0.5) < 1e-9

    def test_zero_vetoes(self):
        c1 = Consideration(
            "ok", lambda s, b: 0.8,
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        c2 = Consideration(
            "veto", lambda s, b: -1.0,  # will evaluate to 0 via clamp
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        action = Action("test", [c1, c2], weight=1.0)
        state = CognitiveState()
        bb = Blackboard()
        assert action.score(state, bb) == 0.0

    def test_empty_considerations(self):
        action = Action("empty", [])
        assert action.score(CognitiveState(), Blackboard()) == 0.0

    def test_weight_applied(self):
        c = Consideration(
            "full", lambda s, b: 1.0,
            ResponseCurve(CurveType.LINEAR, slope=1.0, shift=0.0),
        )
        action = Action("weighted", [c], weight=0.5)
        score = action.score(CognitiveState(), Blackboard())
        assert abs(score - 0.5) < 1e-9


# ---------------------------------------------------------------------------
# UtilityAI tests
# ---------------------------------------------------------------------------

class TestUtilityAI:
    def test_select_highest(self):
        uai = UtilityAI()
        uai.register_action(Action("low", [
            Consideration("x", lambda s, b: 0.2,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]))
        uai.register_action(Action("high", [
            Consideration("x", lambda s, b: 0.9,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]))
        result = uai.select_action(CognitiveState(), Blackboard())
        assert result is not None
        assert result.action_name == "high"

    def test_no_actions_returns_none(self):
        uai = UtilityAI()
        assert uai.select_action(CognitiveState(), Blackboard()) is None

    def test_score_all_sorted(self):
        uai = UtilityAI()
        uai.register_action(Action("b", [
            Consideration("x", lambda s, b: 0.5,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]))
        uai.register_action(Action("a", [
            Consideration("x", lambda s, b: 0.9,
                          ResponseCurve(CurveType.LINEAR, 1.0, 0.0)),
        ]))
        scores = uai.score_all(CognitiveState(), Blackboard())
        assert scores[0].score >= scores[1].score
