"""
types.py — Shared data structures for the Hybrid Utility AI + Blackboard Architecture.

All vectors use continuous floating-point values clamped to [-1.0, 1.0].
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Any


def _clamp(value: float, lo: float = -1.0, hi: float = 1.0) -> float:
    """Clamp *value* to [lo, hi]."""
    return max(lo, min(hi, value))


# ---------------------------------------------------------------------------
# PAD (Pleasure-Arousal-Dominance) Vector
# ---------------------------------------------------------------------------

@dataclass
class PADVector:
    """Continuous emotional coordinate in PAD space, each axis ∈ [-1, 1]."""

    pleasure: float = 0.0
    arousal: float = 0.0
    dominance: float = 0.0

    def __post_init__(self) -> None:
        self.pleasure = _clamp(self.pleasure)
        self.arousal = _clamp(self.arousal)
        self.dominance = _clamp(self.dominance)

    # --- arithmetic helpers --------------------------------------------------

    def __add__(self, other: PADVector) -> PADVector:
        return PADVector(
            self.pleasure + other.pleasure,
            self.arousal + other.arousal,
            self.dominance + other.dominance,
        )

    def __mul__(self, scalar: float) -> PADVector:
        return PADVector(
            self.pleasure * scalar,
            self.arousal * scalar,
            self.dominance * scalar,
        )

    def __rmul__(self, scalar: float) -> PADVector:
        return self.__mul__(scalar)

    def magnitude(self) -> float:
        return math.sqrt(
            self.pleasure ** 2 + self.arousal ** 2 + self.dominance ** 2
        )

    def lerp(self, target: PADVector, t: float) -> PADVector:
        """Linear interpolation toward *target* by factor *t* ∈ [0, 1]."""
        t = _clamp(t, 0.0, 1.0)
        return PADVector(
            self.pleasure + (target.pleasure - self.pleasure) * t,
            self.arousal + (target.arousal - self.arousal) * t,
            self.dominance + (target.dominance - self.dominance) * t,
        )

    def as_tuple(self) -> tuple[float, float, float]:
        return (self.pleasure, self.arousal, self.dominance)

    def __repr__(self) -> str:
        return (
            f"PAD(P={self.pleasure:+.3f}, "
            f"A={self.arousal:+.3f}, "
            f"D={self.dominance:+.3f})"
        )


# ---------------------------------------------------------------------------
# Relationship Vector (per-entity)
# ---------------------------------------------------------------------------

@dataclass
class RelationshipVector:
    """Multi-dimensional relationship descriptor, each axis ∈ [-1, 1].

    Positive values represent favorable standing on that dimension;
    negative values represent unfavorable standing.
    """

    trust: float = 0.0
    fear: float = 0.0
    respect: float = 0.0
    familiarity: float = 0.0
    affection: float = 0.0

    def __post_init__(self) -> None:
        self.trust = _clamp(self.trust)
        self.fear = _clamp(self.fear)
        self.respect = _clamp(self.respect)
        self.familiarity = _clamp(self.familiarity)
        self.affection = _clamp(self.affection)

    def update(self, dimension: str, delta: float) -> None:
        """Shift a single dimension by *delta*, clamped to [-1, 1]."""
        if not hasattr(self, dimension):
            raise ValueError(f"Unknown relationship dimension: {dimension!r}")
        current = getattr(self, dimension)
        setattr(self, dimension, _clamp(current + delta))

    def as_dict(self) -> dict[str, float]:
        return {
            "trust": self.trust,
            "fear": self.fear,
            "respect": self.respect,
            "familiarity": self.familiarity,
            "affection": self.affection,
        }

    def __repr__(self) -> str:
        parts = ", ".join(f"{k}={v:+.2f}" for k, v in self.as_dict().items())
        return f"Rel({parts})"


# ---------------------------------------------------------------------------
# Stimulus (incoming event)
# ---------------------------------------------------------------------------

@dataclass
class Stimulus:
    """An incoming event / signal directed at the AI agent.

    Attributes:
        entity_id:  Who caused the stimulus (Blackboard entity key).
        event_type: Categorical label (e.g. 'greeting', 'insult', 'gift').
        intensity:  Strength of the event, ∈ [0, 1].
        tags:       Arbitrary metadata for the appraisal layer.
    """

    entity_id: str
    event_type: str
    intensity: float = 0.5
    tags: dict[str, Any] = field(default_factory=dict)

    def __post_init__(self) -> None:
        self.intensity = _clamp(self.intensity, 0.0, 1.0)


# ---------------------------------------------------------------------------
# Action Score (output of Utility AI)
# ---------------------------------------------------------------------------

@dataclass
class ActionScore:
    """An evaluated action with its utility score."""

    action_name: str
    score: float

    def __repr__(self) -> str:
        return f"Action({self.action_name!r}, score={self.score:.4f})"


# ---------------------------------------------------------------------------
# Cognitive State (snapshot after cognitive processing)
# ---------------------------------------------------------------------------

@dataclass
class CognitiveState:
    """Bundled output of the cognitive processing pipeline.

    Captures everything the Utility AI needs to score actions.
    """

    emotion: PADVector = field(default_factory=PADVector)
    mood: PADVector = field(default_factory=PADVector)
    trait_modifiers: dict[str, float] = field(default_factory=dict)
    appraisal_context: dict[str, float] = field(default_factory=dict)
    source_entity_id: str = ""

    def __repr__(self) -> str:
        return (
            f"CogState(emotion={self.emotion}, mood={self.mood}, "
            f"entity={self.source_entity_id!r})"
        )
