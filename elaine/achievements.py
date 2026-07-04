"""Achievements / milestones — a pure read over a live AffectEngine's Self + metrics.

Deterministic, side-effect free: given the current state it returns the list of unlocked
badges. Used by the REPL `/achievements` command. Adding one is just a rule below.
"""
from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Badge:
    id: str
    title: str
    desc: str


# (id, title, description, predicate(engine) -> bool)
_RULES = [
    ("hello", "Introduced", "told her your name",
     lambda e: e.memory.has("name") and e.memory.get("name") != "friend"),
    ("chatterbox", "Chatterbox", "10+ messages in one session",
     lambda e: e.memory.turn >= 10),
    ("clingy", "Clingy", "50+ messages in one session",
     lambda e: e.memory.turn >= 50),
    ("menace", "Menace", "insulted her 3+ times",
     lambda e: e.self_state.registers.times_insulted >= 3),
    ("public_enemy", "Public Enemy", "insulted her 10+ times",
     lambda e: e.self_state.registers.times_insulted >= 10),
    ("nemesis", "Nemesis", "landed on her bad side",
     lambda e: e.self_state.registers.role == "nemesis"),
    ("favorite", "Teacher's Pet", "became her favorite",
     lambda e: e.self_state.registers.role == "favorite"),
    ("cooldown", "Time-Out", "made her rage-quit into COOLDOWN",
     lambda e: e.current == "COOLDOWN"),
    ("smug", "Ego Boost", "flattered her into SMUG mode",
     lambda e: e.self_state.last_mode == "SMUG"),
    ("gamer", "Game On", "played a game with her",
     lambda e: any(e.metrics.intents.get(k, 0) for k in
                   ("COIN", "DICE", "EIGHTBALL", "RPS_START", "RATE", "WOULD_RATHER"))),
    ("bff", "Grudging Respect", "got her to actually like you",
     lambda e: e.self_state.registers.relationship >= 8),
]


def unlocked(engine) -> list[Badge]:
    """Return the badges currently unlocked for this engine's session."""
    out = []
    for bid, title, desc, pred in _RULES:
        try:
            if pred(engine):
                out.append(Badge(bid, title, desc))
        except Exception:  # noqa: BLE001 — a badge rule must never break the REPL
            continue
    return out
