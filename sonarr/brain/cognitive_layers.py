"""
cognitive_layers.py — Pillar 3: Cognitive Layers (Time-Scaled).

Four hierarchical processing tiers:
    1. Traits     (permanent) — fixed personality modifiers
    2. Mood       (long-term) — exponential moving average of emotion
    3. Emotion    (short-term) — immediate PAD reaction
    4. Appraisal  (contextual) — entity-relationship-aware modulation
"""

from __future__ import annotations

from dataclasses import dataclass, field

from .core_types import PADVector, Stimulus, CognitiveState, _clamp
from .blackboard import Blackboard
from .emotion_engine import EmotionEngine


# ---------------------------------------------------------------------------
# Trait Definitions
# ---------------------------------------------------------------------------

@dataclass
class TraitProfile:
    """Permanent personality modifiers.

    Each trait acts as a multiplier on the corresponding PAD axis of
    incoming impulses.  A value of 1.0 is neutral; >1 amplifies, <1 dampens.

    Additional named modifiers can be stored in *extras* for use by
    the Utility AI or custom appraisal logic.
    """

    pleasure_mod: float = 1.0    # e.g. optimism / pessimism
    arousal_mod: float = 1.0     # e.g. excitability / stoicism
    dominance_mod: float = 1.0   # e.g. assertiveness / submissiveness

    # Arbitrary named modifiers (e.g. "irritability": 0.7)
    extras: dict[str, float] = field(default_factory=dict)

    def apply(self, impulse: PADVector) -> PADVector:
        """Scale a PAD impulse through personality filters."""
        return PADVector(
            impulse.pleasure * self.pleasure_mod,
            impulse.arousal * self.arousal_mod,
            impulse.dominance * self.dominance_mod,
        )


# ---------------------------------------------------------------------------
# Mood Tracker
# ---------------------------------------------------------------------------

class MoodTracker:
    """Long-term emotional trend via exponential moving average.

    ``mood = α * current_emotion + (1 - α) * previous_mood``

    Lower α → slower mood drift (more stable personality).
    Higher α → mood closely tracks recent emotions.
    """

    def __init__(self, alpha: float = 0.1) -> None:
        self.alpha: float = _clamp(alpha, 0.01, 1.0)
        self.mood: PADVector = PADVector()

    def update(self, current_emotion: PADVector) -> PADVector:
        """Blend *current_emotion* into the running mood average."""
        self.mood = self.mood.lerp(current_emotion, self.alpha)
        return PADVector(*self.mood.as_tuple())  # return copy

    def decay(self, dt: float) -> None:
        """Very slow drift toward neutral — moods aren't permanent."""
        import math
        factor = math.exp(-0.02 * dt)  # much slower than emotion decay
        self.mood = self.mood * factor

    def get_mood(self) -> PADVector:
        return PADVector(*self.mood.as_tuple())


# ---------------------------------------------------------------------------
# Appraisal Layer
# ---------------------------------------------------------------------------

class AppraisalLayer:
    """Contextualizes a raw PAD impulse based on *who* triggered it.

    Uses the entity's relationship vector from the Blackboard to compute
    context multipliers.  E.g., an insult from a trusted friend hurts more
    on the pleasure axis but triggers less fear.
    """

    @staticmethod
    def appraise(
        impulse: PADVector,
        entity_id: str,
        blackboard: Blackboard,
    ) -> tuple[PADVector, dict[str, float]]:
        """Return ``(modulated_impulse, appraisal_context)``."""
        record = blackboard.get_entity(entity_id)
        context: dict[str, float] = {}

        if record is None:
            # Unknown entity -> return unmodified impulse
            context["known"] = 0.0
            return impulse, context

        rel = record.relationship
        context["known"] = 1.0
        context["trust"] = rel.trust
        context["fear"] = rel.fear
        context["respect"] = rel.respect
        context["familiarity"] = rel.familiarity
        context["affection"] = rel.affection

        # --- modulation rules ------------------------------------------------
        # Trust amplifies pleasure impact (positive or negative)
        pleasure_scale = 1.0 + 0.5 * rel.trust
        # Fear amplifies arousal
        arousal_scale = 1.0 + 0.4 * abs(rel.fear)
        # Respect modulates dominance (respected entity -> feel less dominant)
        dominance_scale = 1.0 - 0.3 * rel.respect
        # Familiarity dampens overall intensity (used to them)
        familiarity_dampen = 1.0 - 0.2 * max(0.0, rel.familiarity)

        modulated = PADVector(
            impulse.pleasure * pleasure_scale * familiarity_dampen,
            impulse.arousal * arousal_scale * familiarity_dampen,
            impulse.dominance * dominance_scale * familiarity_dampen,
        )

        context["pleasure_scale"] = pleasure_scale
        context["arousal_scale"] = arousal_scale
        context["dominance_scale"] = dominance_scale
        context["familiarity_dampen"] = familiarity_dampen

        return modulated, context


# ---------------------------------------------------------------------------
# Cognitive Processor (orchestrates all layers)
# ---------------------------------------------------------------------------

class CognitiveProcessor:
    """Orchestrates the four cognitive tiers into a single processing pipeline.

    Pipeline for each stimulus:
        1. **Appraisal** → who is this from?  Scale impulse by relationship.
        2. **Traits** → apply permanent personality modifiers.
        3. **Emotion** → feed modified impulse into EmotionEngine.
        4. **Mood** → update long-term rolling average.
        5. Return a bundled :class:`CognitiveState`.
    """

    def __init__(
        self,
        traits: TraitProfile | None = None,
        mood_alpha: float = 0.1,
    ) -> None:
        self.traits = traits or TraitProfile()
        self.mood_tracker = MoodTracker(alpha=mood_alpha)

    def process_stimulus(
        self,
        stimulus: Stimulus,
        blackboard: Blackboard,
        emotion_engine: EmotionEngine,
    ) -> CognitiveState:
        """Run a stimulus through all four cognitive layers.

        Returns a :class:`CognitiveState` snapshot for the Utility AI.
        """
        # 1. Emotion Engine: get raw PAD impulse for this event type
        raw_impulse = emotion_engine.map_stimulus_to_pad(stimulus)

        # 2. Appraisal: modulate by entity relationship
        appraised_impulse, appraisal_context = AppraisalLayer.appraise(
            raw_impulse, stimulus.entity_id, blackboard
        )

        # 3. Traits: apply personality modifiers
        trait_modified = self.traits.apply(appraised_impulse)

        # 4. Emotion: apply the fully modulated impulse
        emotion_engine.apply_impulse(trait_modified, stimulus.intensity)

        # 5. Mood: update rolling average with new emotional state
        current_emotion = emotion_engine.get_state()
        current_mood = self.mood_tracker.update(current_emotion)

        # Record interaction on Blackboard
        blackboard.record_interaction(stimulus.entity_id, stimulus)

        return CognitiveState(
            emotion=current_emotion,
            mood=current_mood,
            trait_modifiers={
                "pleasure_mod": self.traits.pleasure_mod,
                "arousal_mod": self.traits.arousal_mod,
                "dominance_mod": self.traits.dominance_mod,
                **self.traits.extras,
            },
            appraisal_context=appraisal_context,
            source_entity_id=stimulus.entity_id,
        )

    def tick(self, dt: float, emotion_engine: EmotionEngine) -> None:
        """Time-step: decay emotion and drift mood."""
        emotion_engine.decay(dt)
        self.mood_tracker.decay(dt)

    def __repr__(self) -> str:
        return (
            f"CognitiveProcessor(traits={self.traits}, "
            f"mood={self.mood_tracker.get_mood()})"
        )
