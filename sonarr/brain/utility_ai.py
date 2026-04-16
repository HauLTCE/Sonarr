"""
utility_ai.py — Pillar 4: Utility AI (Output).

Curve-based action scoring replaces all IF/THEN logic.  Each possible
action is evaluated by multiplying the scores of its constituent
considerations, and the highest-scoring action is selected.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from enum import Enum
from typing import Callable

from .core_types import CognitiveState, ActionScore, _clamp
from .blackboard import Blackboard


# ---------------------------------------------------------------------------
# Response Curve Types
# ---------------------------------------------------------------------------

class CurveType(Enum):
    LINEAR = "linear"
    QUADRATIC = "quadratic"
    LOGISTIC = "logistic"
    STEP = "step"
    INVERSE = "inverse"


@dataclass
class ResponseCurve:
    """Mathematical mapping from an input value to a [0, 1] score.

    Parameters:
        curve_type: Shape of the curve.
        slope:      Steepness / direction of the curve.
        shift:      Horizontal offset (threshold for step curves).
        exponent:   Power for quadratic curves.
    """

    curve_type: CurveType = CurveType.LINEAR
    slope: float = 1.0
    shift: float = 0.0
    exponent: float = 2.0

    def evaluate(self, x: float) -> float:
        """Evaluate the curve at input *x*, returning a score ∈ [0, 1]."""
        match self.curve_type:
            case CurveType.LINEAR:
                y = self.slope * x + self.shift
            case CurveType.QUADRATIC:
                y = self.slope * (abs(x - self.shift) ** self.exponent)
            case CurveType.LOGISTIC:
                try:
                    y = 1.0 / (1.0 + math.exp(-self.slope * (x - self.shift)))
                except OverflowError:
                    y = 0.0 if self.slope * (x - self.shift) < 0 else 1.0
            case CurveType.STEP:
                y = 1.0 if x > self.shift else 0.0
            case CurveType.INVERSE:
                # Inverse relationship: high input -> low score
                y = 1.0 - _clamp(self.slope * x + self.shift, 0.0, 1.0)
            case _:
                y = x
        return _clamp(y, 0.0, 1.0)

    def __repr__(self) -> str:
        return (
            f"Curve({self.curve_type.value}, "
            f"slope={self.slope}, shift={self.shift})"
        )


# ---------------------------------------------------------------------------
# Consideration (one axis of evaluation)
# ---------------------------------------------------------------------------

# Type alias for the function that extracts a float from the current context.
InputFn = Callable[[CognitiveState, Blackboard], float]


@dataclass
class Consideration:
    """One axis of evaluation for an action.

    Combines an *input function* (extracts a raw value from the current
    world state) with a *response curve* (maps that value to a [0, 1] score).
    """

    name: str
    input_fn: InputFn
    curve: ResponseCurve

    def score(self, state: CognitiveState, blackboard: Blackboard) -> float:
        """Extract the input value and evaluate through the curve."""
        raw = self.input_fn(state, blackboard)
        return self.curve.evaluate(raw)

    def __repr__(self) -> str:
        return f"Consideration({self.name!r}, {self.curve})"


# ---------------------------------------------------------------------------
# Action (scored by considerations)
# ---------------------------------------------------------------------------

@dataclass
class Action:
    """A possible action the AI can take.

    Score = weight × ∏(consideration scores).
    The multiplicative aggregation means **any single zero-score
    consideration vetoes the entire action**.
    """

    name: str
    considerations: list[Consideration] = field(default_factory=list)
    weight: float = 1.0

    def score(self, state: CognitiveState, blackboard: Blackboard) -> float:
        """Compute the final utility score for this action."""
        if not self.considerations:
            return 0.0
        result = self.weight
        for c in self.considerations:
            s = c.score(state, blackboard)
            result *= s
            if result <= 0:
                return 0.0  # early-out: already vetoed
        return result

    def score_breakdown(
        self, state: CognitiveState, blackboard: Blackboard
    ) -> dict[str, float]:
        """Return per-consideration scores for debugging."""
        return {c.name: c.score(state, blackboard) for c in self.considerations}


# ---------------------------------------------------------------------------
# Utility AI (action selector)
# ---------------------------------------------------------------------------

class UtilityAI:
    """Scores all registered actions and selects the highest-scoring one.

    No IF/THEN logic — everything is continuous curve evaluation.
    """

    def __init__(self) -> None:
        self._actions: list[Action] = []

    def register_action(self, action: Action) -> None:
        """Add an action to the evaluation pool."""
        self._actions.append(action)

    def register_actions(self, *actions: Action) -> None:
        """Convenience: register multiple actions at once."""
        for a in actions:
            self.register_action(a)

    def score_all(
        self, state: CognitiveState, blackboard: Blackboard
    ) -> list[ActionScore]:
        """Score every registered action.  Returns sorted descending."""
        scores = [
            ActionScore(action_name=a.name, score=a.score(state, blackboard))
            for a in self._actions
        ]
        scores.sort(key=lambda s: s.score, reverse=True)
        return scores

    def select_action(
        self, state: CognitiveState, blackboard: Blackboard
    ) -> ActionScore | None:
        """Return the highest-scoring action, or None if none are registered."""
        scored = self.score_all(state, blackboard)
        return scored[0] if scored else None

    def debug_breakdown(
        self, state: CognitiveState, blackboard: Blackboard
    ) -> dict[str, dict[str, float]]:
        """Per-action, per-consideration score breakdown for debugging."""
        return {
            a.name: a.score_breakdown(state, blackboard)
            for a in self._actions
        }

    @property
    def action_names(self) -> list[str]:
        return [a.name for a in self._actions]

    def __repr__(self) -> str:
        return f"UtilityAI(actions={self.action_names})"


# ---------------------------------------------------------------------------
# Pre-built input function helpers
# ---------------------------------------------------------------------------

def pleasure_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract current pleasure value (normalized to [0, 1] from [-1, 1])."""
    return (state.emotion.pleasure + 1.0) / 2.0

def arousal_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract current arousal value (normalized to [0, 1])."""
    return (state.emotion.arousal + 1.0) / 2.0

def dominance_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract current dominance value (normalized to [0, 1])."""
    return (state.emotion.dominance + 1.0) / 2.0

def mood_pleasure_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract mood-level pleasure (normalized to [0, 1])."""
    return (state.mood.pleasure + 1.0) / 2.0

def entity_trust_input(state: CognitiveState, bb: Blackboard) -> float:
    """Extract trust toward the source entity (normalized to [0, 1])."""
    eid = state.source_entity_id
    record = bb.get_entity(eid)
    if record is None:
        return 0.5  # neutral for unknown entities
    return (record.relationship.trust + 1.0) / 2.0

def entity_fear_input(state: CognitiveState, bb: Blackboard) -> float:
    """Extract fear of the source entity (normalized to [0, 1])."""
    eid = state.source_entity_id
    record = bb.get_entity(eid)
    if record is None:
        return 0.5
    return (record.relationship.fear + 1.0) / 2.0

def entity_respect_input(state: CognitiveState, bb: Blackboard) -> float:
    """Extract respect for the source entity (normalized to [0, 1])."""
    eid = state.source_entity_id
    record = bb.get_entity(eid)
    if record is None:
        return 0.5
    return (record.relationship.respect + 1.0) / 2.0

def emotional_intensity_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract the magnitude of the current emotion (0 to ~1.7)."""
    return state.emotion.magnitude() / 1.732  # normalize by max possible
