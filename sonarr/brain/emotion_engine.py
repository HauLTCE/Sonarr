"""
emotion_engine.py — Pillar 2: Dimensional Emotion Engine.

Implements a continuous PAD (Pleasure-Arousal-Dominance) model.
Emotional state lives in a 3D vector space — no discrete labels drive logic.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Any

from .core_types import PADVector, Stimulus, _clamp


# ---------------------------------------------------------------------------
# Default stimulus → PAD mapping table
# ---------------------------------------------------------------------------

# Each entry: event_type → base PAD impulse (before intensity scaling).
DEFAULT_PAD_MAP: dict[str, tuple[float, float, float]] = {
    # Positive events
    "greeting":     (+0.4, +0.2, +0.1),
    "compliment":   (+0.6, +0.3, +0.2),
    "gift":         (+0.7, +0.3, +0.1),
    "joke":         (+0.5, +0.4, +0.1),
    "agreement":    (+0.3, -0.1, +0.2),
    "apology":      (+0.3, -0.2, +0.3),
    "support":      (+0.5, +0.1, +0.2),
    # Negative events
    "insult":       (-0.6, +0.7, -0.3),
    "threat":       (-0.7, +0.8, -0.5),
    "betrayal":     (-0.9, +0.6, -0.6),
    "dismissal":    (-0.4, -0.2, -0.4),
    "criticism":    (-0.4, +0.3, -0.2),
    "rejection":    (-0.6, +0.2, -0.5),
    # Neutral / ambiguous
    "neutral":      (+0.0, +0.0, +0.0),
    "question":     (+0.1, +0.2, +0.0),
    "observation":  (+0.1, +0.1, +0.0),
    "command":      (-0.1, +0.3, -0.3),
}


# ---------------------------------------------------------------------------
# PAD Octant → Label mapping (informational only)
# ---------------------------------------------------------------------------

_OCTANT_LABELS: dict[tuple[bool, bool, bool], str] = {
    (True,  True,  True):  "Exuberant",
    (True,  True,  False): "Dependent",
    (True,  False, True):  "Relaxed",
    (True,  False, False): "Docile",
    (False, True,  True):  "Hostile",
    (False, True,  False): "Anxious",
    (False, False, True):  "Disdainful",
    (False, False, False): "Bored",
}


def classify_pad(pad: PADVector) -> str:
    """Map a PAD vector to a human-readable label via octant lookup.

    This is purely **informational** — no game logic should branch on it.
    """
    key = (pad.pleasure >= 0, pad.arousal >= 0, pad.dominance >= 0)
    return _OCTANT_LABELS[key]


# ---------------------------------------------------------------------------
# Emotion Engine
# ---------------------------------------------------------------------------

class EmotionEngine:
    """Manages the agent's current emotional state in continuous PAD space.

    Parameters:
        reactivity: Global multiplier on incoming impulses (personality knob).
        decay_rate: Per-second exponential decay speed toward neutral.
        pad_map:    Optional custom stimulus → PAD mapping table.
    """

    def __init__(
        self,
        reactivity: float = 1.0,
        decay_rate: float = 0.3,
        pad_map: dict[str, tuple[float, float, float]] | None = None,
    ) -> None:
        self.reactivity: float = _clamp(reactivity, 0.0, 5.0)
        self.decay_rate: float = _clamp(decay_rate, 0.01, 10.0)
        self.state: PADVector = PADVector()  # starts neutral
        self._pad_map = pad_map or dict(DEFAULT_PAD_MAP)

    # --- core operations -----------------------------------------------------

    def map_stimulus_to_pad(self, stimulus: Stimulus) -> PADVector:
        """Convert a :class:`Stimulus` into a raw PAD impulse.

        Unknown event types produce a zero vector (no emotional impact).
        """
        raw = self._pad_map.get(stimulus.event_type, (0.0, 0.0, 0.0))
        return PADVector(*raw)

    def apply_impulse(self, impulse: PADVector, intensity: float = 1.0) -> None:
        """Blend a PAD impulse into the current emotional state.

        ``new_state = current + impulse * intensity * reactivity``
        """
        scaled = impulse * (intensity * self.reactivity)
        self.state = self.state + scaled

    def decay(self, dt: float) -> None:
        """Exponentially decay toward neutral ``(0, 0, 0)`` over *dt* seconds."""
        if dt <= 0:
            return
        factor = math.exp(-self.decay_rate * dt)
        self.state = self.state * factor

    # --- queries -------------------------------------------------------------

    def classify(self) -> str:
        """Return a human-readable label for the current PAD state."""
        return classify_pad(self.state)

    def get_state(self) -> PADVector:
        """Return a copy of the current emotional state."""
        return PADVector(*self.state.as_tuple())

    # --- convenience ---------------------------------------------------------

    def process_stimulus(self, stimulus: Stimulus) -> PADVector:
        """Map + apply a stimulus in one call.  Returns the impulse used."""
        impulse = self.map_stimulus_to_pad(stimulus)
        self.apply_impulse(impulse, stimulus.intensity)
        return impulse

    def reset(self) -> None:
        """Reset emotional state to neutral."""
        self.state = PADVector()

    def __repr__(self) -> str:
        return f"EmotionEngine(state={self.state}, label={self.classify()!r})"
