"""
actions.py — Behavioral action definitions for Sonarr's Utility AI.

Defines WHAT Sonarr can do in response to stimuli, and the consideration
curves that score each action based on emotional state and relationships.

Actions are scored by the UtilityAI; the highest-scoring action wins.
The winning action name is then used by sonarr_ai.py to select HOW
to respond (which pool of responses to draw from, tone modifiers, etc.).
"""

from __future__ import annotations

from .utility_ai import (
    Action,
    Consideration,
    ResponseCurve,
    CurveType,
    pleasure_input,
    arousal_input,
    dominance_input,
    mood_pleasure_input,
    entity_trust_input,
    entity_respect_input,
    entity_fear_input,
    emotional_intensity_input,
)
from .core_types import CognitiveState
from .blackboard import Blackboard


# =====================================================================
# CUSTOM INPUT FUNCTIONS
# =====================================================================

def mood_arousal_input(state: CognitiveState, _bb: Blackboard) -> float:
    """Extract mood-level arousal (normalized to [0, 1])."""
    return (state.mood.arousal + 1.0) / 2.0


def entity_affection_input(state: CognitiveState, bb: Blackboard) -> float:
    """Extract affection for the source entity (normalized to [0, 1])."""
    eid = state.source_entity_id
    record = bb.get_entity(eid)
    if record is None:
        return 0.5
    return (record.relationship.affection + 1.0) / 2.0


def entity_familiarity_input(state: CognitiveState, bb: Blackboard) -> float:
    """Extract familiarity with the source entity (normalized to [0, 1])."""
    eid = state.source_entity_id
    record = bb.get_entity(eid)
    if record is None:
        return 0.5
    return (record.relationship.familiarity + 1.0) / 2.0


def interaction_count_input(state: CognitiveState, bb: Blackboard) -> float:
    """Normalized interaction count (0-1, saturates at 50 interactions)."""
    record = bb.get_entity(state.source_entity_id)
    if record is None:
        return 0.0
    return min(len(record.interaction_history) / 50.0, 1.0)


# =====================================================================
# ACTION DEFINITIONS
# =====================================================================
# Each action represents a behavioral mode, NOT a response category.
# The classifier category determines WHAT to respond about.
# The action determines HOW to respond (tone, intensity, extras).

def build_sonarr_actions() -> list[Action]:
    """Build and return all of Sonarr's behavioral actions."""

    actions = []

    # -----------------------------------------------------------------
    # 1. RESPOND_COLD (default)
    # -----------------------------------------------------------------
    # Standard Sonarr response — cold, sassy, picks from premade pool.
    # Should score reasonably high in most situations.
    actions.append(Action(
        name="respond_cold",
        weight=1.0,
        considerations=[
            # Always has a baseline score — this is the default
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.4,  # Lowered from 0.7 so it doesn't always win
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # Higher arousal slightly reduces cold score (escalation takes over)
            Consideration(
                name="not_too_aroused",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.6, shift=0.2),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 2. RESPOND_ESCALATED
    # -----------------------------------------------------------------
    # Angrier, harsher response. May include effects (timeout, etc.)
    # Wins when arousal is high and trust is low.
    actions.append(Action(
        name="respond_escalated",
        weight=1.5,  # Boosted slightly
        considerations=[
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.4,
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # Needs high arousal
            Consideration(
                name="high_arousal",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=4.0, shift=0.4), # broader curve
            ),
            # Low trust amplifies
            Consideration(
                name="low_trust",
                input_fn=entity_trust_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.8, shift=0.1),
            ),
            # Needs negative pleasure
            Consideration(
                name="displeased",
                input_fn=pleasure_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.8, shift=0.1),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 3. RESPOND_SASSY
    # -----------------------------------------------------------------
    # Witty / sarcastic response. Wins when she's in a decent mood
    # but gets mildly annoyed.
    actions.append(Action(
        name="respond_sassy",
        weight=1.3,
        considerations=[
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.4,
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # Moderate arousal (not too angry, not too calm)
            Consideration(
                name="moderate_arousal",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.QUADRATIC, slope=-2.0, shift=0.5, exponent=2.0), # softer curve
            ),
            # Dominance feels good (she's in control)
            Consideration(
                name="feels_dominant",
                input_fn=dominance_input,
                curve=ResponseCurve(CurveType.LINEAR, slope=0.8, shift=0.2),
            ),
            # Mood isn't terrible
            Consideration(
                name="decent_mood",
                input_fn=mood_pleasure_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=4.0, shift=0.3),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 4. RESPOND_WARM
    # -----------------------------------------------------------------
    # Rare — Sonarr being actually nice. Only for high-affection users
    # when she's in a good mood.
    actions.append(Action(
        name="respond_warm",
        weight=0.8,  # penalized — warmth is hard-earned
        considerations=[
            # High affection required
            Consideration(
                name="high_affection",
                input_fn=entity_affection_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=8.0, shift=0.7),
            ),
            # Good mood
            Consideration(
                name="good_mood",
                input_fn=mood_pleasure_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=5.0, shift=0.6),
            ),
            # High trust
            Consideration(
                name="trusts_them",
                input_fn=entity_trust_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=5.0, shift=0.6),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 5. IGNORE
    # -----------------------------------------------------------------
    # Don't respond at all. When bored / very dominant / spam.
    actions.append(Action(
        name="ignore",
        weight=0.7,  # penalized — usually better to respond
        considerations=[
            # Very low arousal (bored)
            Consideration(
                name="bored",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=1.0, shift=0.1),
            ),
            # High dominance (doesn't need to respond)
            Consideration(
                name="above_this",
                input_fn=dominance_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=5.0, shift=0.7),
            ),
            # Low familiarity (don't know them, don't care)
            Consideration(
                name="stranger",
                input_fn=entity_familiarity_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.6, shift=0.1),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 6. RESPOND_GRUDGE
    # -----------------------------------------------------------------
    # References a past negative interaction. For users who wronged her
    # and are now trying to be normal.
    actions.append(Action(
        name="respond_grudge",
        weight=1.5,
        considerations=[
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.3,
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # They've interacted before
            Consideration(
                name="known_user",
                input_fn=interaction_count_input,
                curve=ResponseCurve(CurveType.STEP, shift=0.01), # very low threshold
            ),
            # Low trust — they've burned her
            Consideration(
                name="low_trust",
                input_fn=entity_trust_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.9, shift=0.0),
            ),
            # Current stimulus isn't super negative (they're trying to be nice)
            Consideration(
                name="mild_stimulus",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.5, shift=0.2),
            ),
            # Negative mood persists
            Consideration(
                name="bad_mood",
                input_fn=mood_pleasure_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.7, shift=0.1),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 7. RESPOND_POWER_TRIP
    # -----------------------------------------------------------------
    # When someone begs, apologizes, or she's feeling very dominant.
    actions.append(Action(
        name="respond_power_trip",
        weight=1.4,
        considerations=[
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.3,
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # Very high dominance
            Consideration(
                name="very_dominant",
                input_fn=dominance_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=4.0, shift=0.4), # lowered threshold
            ),
            # Not too angry (she's enjoying this)
            Consideration(
                name="not_angry",
                input_fn=pleasure_input,
                curve=ResponseCurve(CurveType.LINEAR, slope=0.6, shift=0.3),
            ),
            # Low respect for them
            Consideration(
                name="low_respect_for_them",
                input_fn=entity_respect_input,
                curve=ResponseCurve(CurveType.INVERSE, slope=0.6, shift=0.1),
            ),
        ],
    ))

    # -----------------------------------------------------------------
    # 8. RESPOND_INTRIGUED
    # -----------------------------------------------------------------
    # Genuinely interested — for philosophy, gossip, meta questions.
    actions.append(Action(
        name="respond_intrigued",
        weight=1.3,
        considerations=[
            Consideration(
                name="baseline",
                input_fn=lambda s, bb: 0.4,
                curve=ResponseCurve(CurveType.LINEAR, slope=1.0),
            ),
            # Moderate positive pleasure
            Consideration(
                name="somewhat_pleased",
                input_fn=pleasure_input,
                curve=ResponseCurve(CurveType.LOGISTIC, slope=3.0, shift=0.3), # lowered shift
            ),
            # Some arousal (interested, not bored)
            Consideration(
                name="interested",
                input_fn=arousal_input,
                curve=ResponseCurve(CurveType.LINEAR, slope=0.8, shift=0.1),
            ),
            # Familiar user gets bonus
            Consideration(
                name="familiar_user",
                input_fn=entity_familiarity_input,
                curve=ResponseCurve(CurveType.LINEAR, slope=0.5, shift=0.3),
            ),
        ],
    ))

    return actions
