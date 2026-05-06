"""
sonarr.brain — Hybrid Utility AI + Blackboard Architecture.

The AI "brain" provides emotional state tracking, per-user relationship
memory, cognitive processing, and utility-based action selection.

Public API:
    AIBrain         — Top-level orchestrator
    Stimulus        — Incoming event descriptor
    ActionScore     — Scored action result
    PADVector       — Emotional coordinate
    RelationshipVector — Per-entity relationship descriptor
    CognitiveState  — Snapshot after cognitive processing
    TraitProfile    — Permanent personality modifiers
"""

from .core_types import (
    PADVector,
    RelationshipVector,
    Stimulus,
    ActionScore,
    CognitiveState,
)
from .blackboard import Blackboard, EntityRecord
from .emotion_engine import EmotionEngine
from .cognitive_layers import CognitiveProcessor, TraitProfile
from .utility_ai import UtilityAI, Action, Consideration, ResponseCurve, CurveType
from .ai_brain import AIBrain
from .integration import BrainMixin

__all__ = [
    "BrainMixin",
    "AIBrain",
    "Blackboard",
    "EntityRecord",
    "EmotionEngine",
    "CognitiveProcessor",
    "TraitProfile",
    "UtilityAI",
    "Action",
    "Consideration",
    "ResponseCurve",
    "CurveType",
    "PADVector",
    "RelationshipVector",
    "Stimulus",
    "ActionScore",
    "CognitiveState",
]
