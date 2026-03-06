"""
ai_brain.py — Orchestrator: wires all four pillars into a single coherent AI.

The AIBrain owns a Blackboard, EmotionEngine, CognitiveProcessor, and
UtilityAI.  It presents one public interface:

    brain.receive_stimulus(stimulus) → ActionScore
    brain.tick(dt)                   → decay / drift
"""

from __future__ import annotations

from .core_types import PADVector, Stimulus, ActionScore, CognitiveState
from .blackboard import Blackboard
from .emotion_engine import EmotionEngine
from .cognitive_layers import CognitiveProcessor, TraitProfile
from .utility_ai import UtilityAI, Action


class AIBrain:
    """Top-level orchestrator for the Hybrid Utility AI system.

    Usage::

        brain = AIBrain(traits=my_traits)
        brain.blackboard.register_entity("alice", ...)
        brain.utility.register_action(my_action)

        result = brain.receive_stimulus(Stimulus("alice", "greeting", 0.8))
        print(result)  # → ActionScore(...)

    Parameters:
        traits:     Permanent personality modifiers.
        reactivity: Emotion engine sensitivity (higher = more reactive).
        decay_rate: Emotion decay speed per second.
        mood_alpha:  Mood EMA blending factor (lower = more stable mood).
    """

    def __init__(
        self,
        traits: TraitProfile | None = None,
        reactivity: float = 1.0,
        decay_rate: float = 0.3,
        mood_alpha: float = 0.1,
    ) -> None:
        self.blackboard = Blackboard()
        self.emotion_engine = EmotionEngine(
            reactivity=reactivity,
            decay_rate=decay_rate,
        )
        self.cognitive = CognitiveProcessor(
            traits=traits,
            mood_alpha=mood_alpha,
        )
        self.utility = UtilityAI()

        # Last cognitive state (for inspection)
        self._last_state: CognitiveState | None = None

    # --- main API ------------------------------------------------------------

    def receive_stimulus(self, stimulus: Stimulus) -> ActionScore | None:
        """Full pipeline: stimulus → cognitive processing → action selection.

        Returns the highest-scoring :class:`ActionScore`, or *None* if no
        actions are registered.
        """
        # 1. Process through cognitive layers
        state = self.cognitive.process_stimulus(
            stimulus, self.blackboard, self.emotion_engine
        )
        self._last_state = state

        # 2. Score and select action
        return self.utility.select_action(state, self.blackboard)

    def receive_stimulus_full(
        self, stimulus: Stimulus
    ) -> tuple[ActionScore | None, CognitiveState, list[ActionScore]]:
        """Like :meth:`receive_stimulus` but returns full diagnostic data.

        Returns ``(selected_action, cognitive_state, all_scores)``.
        """
        state = self.cognitive.process_stimulus(
            stimulus, self.blackboard, self.emotion_engine
        )
        self._last_state = state
        all_scores = self.utility.score_all(state, self.blackboard)
        selected = all_scores[0] if all_scores else None
        return selected, state, all_scores

    def tick(self, dt: float) -> None:
        """Advance time: decay emotions, drift mood.

        Call once per frame / update cycle.
        """
        self.cognitive.tick(dt, self.emotion_engine)

    # --- inspection ----------------------------------------------------------

    @property
    def last_state(self) -> CognitiveState | None:
        """The cognitive state produced by the most recent stimulus."""
        return self._last_state

    @property
    def current_emotion(self) -> PADVector:
        return self.emotion_engine.get_state()

    @property
    def current_mood(self) -> PADVector:
        return self.cognitive.mood_tracker.get_mood()

    @property
    def emotion_label(self) -> str:
        return self.emotion_engine.classify()

    def debug_snapshot(self) -> dict:
        """Full read-only snapshot of internal state."""
        return {
            "emotion": self.emotion_engine.get_state().as_tuple(),
            "emotion_label": self.emotion_label,
            "mood": self.current_mood.as_tuple(),
            "blackboard": self.blackboard.snapshot(),
            "last_cognitive_state": repr(self._last_state),
            "registered_actions": self.utility.action_names,
        }

    def __repr__(self) -> str:
        return (
            f"AIBrain(emotion={self.current_emotion}, "
            f"mood={self.current_mood}, "
            f"label={self.emotion_label!r})"
        )
