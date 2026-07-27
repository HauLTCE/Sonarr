"""Affect & relational model (AFFECT_MODEL.md, Plan Phase 9).

Self = (dialogue_state, R, episodic_log). This module owns R: a fixed-width register
vector, per-turn decay toward baselines, top-down first-match mode evaluation, global
bleed, anger momentum, hysteresis flips, and persistent favorite/nemesis roles.

All driven by the `personality:` config block in the YAML. No wall-clock — decay uses
the logical clock (turn count). No model writes a register; the sentiment label (a
symbolic classifier output) is the only "sensing" input, consistent with §8.

Enhancements folded in (2026-06-30):
  #1 favorite/nemesis roles — persistent named roles re-derived from relationship/grudge.
  #4 repetition->affect — exact repeated input spikes boredom/anger (read from the ring
     buffer, NOT from transition guards, preserving the DESIGN §5 invariant).
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any

# Default personality (overridable by the YAML personality: block).
DEFAULT_BASELINES = {"anger": 0, "warmth": 3, "amusement": 2, "boredom": 0, "confidence": 6, "energy": 5}
DEFAULT_DECAY = {"anger": 0.5, "warmth": 0.2, "amusement": 0.4, "boredom": 0.3, "confidence": 0.1, "energy": 0.25}
_CONTINUOUS = set(DEFAULT_BASELINES)
_RANGE = (0.0, 10.0)


def _clamp(v: float, lo: float = _RANGE[0], hi: float = _RANGE[1]) -> float:
    return max(lo, min(hi, v))


@dataclass
class Personality:
    baselines: dict[str, float] = field(default_factory=lambda: dict(DEFAULT_BASELINES))
    decay: dict[str, float] = field(default_factory=lambda: dict(DEFAULT_DECAY))
    bleed: float = 0.4                       # global_to_user weight (§5)
    flips: dict[str, dict[str, str]] = field(default_factory=dict)   # hysteresis
    cooldown_enter_anger: float = 9.0
    cooldown_duration: int = 5
    decay_cap: int = 50

    @classmethod
    def from_config(cls, cfg: dict[str, Any]) -> "Personality":
        cfg = cfg or {}
        p = cls()
        p.baselines = {**DEFAULT_BASELINES, **(cfg.get("baselines") or {})}
        p.decay = {**DEFAULT_DECAY, **(cfg.get("decay") or {})}
        bleed = cfg.get("bleed") or {}
        p.bleed = float(bleed.get("global_to_user", 0.4))
        p.flips = cfg.get("flips") or {}
        cd = cfg.get("cooldown") or {}
        p.cooldown_enter_anger = float(cd.get("enter_anger", 9))
        p.cooldown_duration = int(cd.get("duration_msgs", 5))
        p.decay_cap = int(cfg.get("decay_cap", 50))
        return p


@dataclass
class Registers:
    """The mutable register vector + relational state for one user."""

    # Elaine's mood (continuous, 0-10)
    anger: float = 0.0
    warmth: float = 3.0
    amusement: float = 2.0
    boredom: float = 0.0
    confidence: float = 6.0
    energy: float = 5.0
    # Her model of you
    relationship: float = 0.0          # -10..10
    trust: float = 5.0                 # 0..10
    your_sentiment: str = "NEUTRAL"
    times_insulted: int = 0
    grudge: bool = False
    role: str = "stranger"             # enhancement #1: stranger | favorite | nemesis
    # derived/bookkeeping
    anger_delta: float = 0.0
    _prev_anger: float = 0.0

    def cont(self) -> dict[str, float]:
        return {k: getattr(self, k) for k in _CONTINUOUS}

    def to_dict(self) -> dict[str, Any]:
        return {
            "anger": self.anger, "warmth": self.warmth, "amusement": self.amusement,
            "boredom": self.boredom, "confidence": self.confidence, "energy": self.energy,
            "relationship": self.relationship, "trust": self.trust,
            "your_sentiment": self.your_sentiment, "times_insulted": self.times_insulted,
            "grudge": self.grudge, "role": self.role,
            "anger_delta": self.anger_delta, "_prev_anger": self._prev_anger,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Registers":
        r = cls()
        for k, v in (d or {}).items():
            if hasattr(r, k):
                setattr(r, k, v)
        return r

    @classmethod
    def fresh(cls, p: Personality) -> "Registers":
        b = p.baselines
        return cls(
            anger=b["anger"], warmth=b["warmth"], amusement=b["amusement"],
            boredom=b["boredom"], confidence=b["confidence"], energy=b["energy"],
        )


def decay_step(r: Registers, p: Personality, steps: int = 1) -> None:
    """Drift each continuous register toward its baseline. grudge never decays.

    Exponential relaxation r += (baseline - r) * rate, applied `steps` times (catch-up
    decay on load uses steps>1). relationship/trust are history, not mood — they decay
    very slowly (handled by their absence from the fast set: we only relax the mood axes).
    """
    for _ in range(max(0, steps)):
        for key in _CONTINUOUS:
            base = p.baselines[key]
            rate = p.decay.get(key, 0.0)
            val = getattr(r, key)
            setattr(r, key, _clamp(val + (base - val) * rate))


def momentum_update(r: Registers) -> None:
    """Record the per-turn anger delta, then snapshot for next turn (§5 momentum)."""
    r.anger_delta = r.anger - r._prev_anger
    r._prev_anger = r.anger


# ----------------------------------------------------------------------------
# Mode expression evaluation. Tiny, safe (no eval): "<reg> <op> <number>".
# ----------------------------------------------------------------------------

_CMP_RE = re.compile(r"^\s*([a-z_]+)\s*(<=|>=|<|>|==|!=)\s*(-?\d+(?:\.\d+)?)\s*$")
_OPS = {
    "<": lambda a, b: a < b, "<=": lambda a, b: a <= b,
    ">": lambda a, b: a > b, ">=": lambda a, b: a >= b,
    "==": lambda a, b: a == b, "!=": lambda a, b: a != b,
}


def _readable(r: Registers, eff_anger: float) -> dict[str, float]:
    d = {k: getattr(r, k) for k in _CONTINUOUS}
    d["relationship"] = r.relationship
    d["trust"] = r.trust
    d["times_insulted"] = r.times_insulted
    d["anger_delta"] = r.anger_delta
    d["anger"] = eff_anger  # effective (post-bleed) anger
    return d


def _eval_atom(expr: str, vals: dict[str, float]) -> bool:
    m = _CMP_RE.match(expr)
    if not m:
        raise ValueError(f"bad mode expression {expr!r}")
    reg, op, num = m.group(1), m.group(2), float(m.group(3))
    if reg not in vals:
        raise ValueError(f"unknown register {reg!r} in mode expression")
    return _OPS[op](vals[reg], num)


def _eval_clause(clause: dict, vals: dict[str, float]) -> bool:
    if "always" in clause:
        return bool(clause["always"])
    if "all" in clause:
        return all(_eval_atom(e, vals) for e in clause["all"])
    if "any" in clause:
        return any(_eval_atom(e, vals) for e in clause["any"])
    raise ValueError(f"mode clause needs all/any/always, got {list(clause)}")


def effective_anger(r: Registers, global_mood: float, p: Personality) -> float:
    """Read-time blend of per-user anger and the room's negative mood (§5 global bleed)."""
    return _clamp(r.anger + p.bleed * max(0.0, -global_mood))


def evaluate_mode(r: Registers, modes: dict, global_mood: float, p: Personality) -> str:
    """Top-down first-match mode evaluation. Returns the active mode name (NEUTRAL last)."""
    vals = _readable(r, effective_anger(r, global_mood, p))
    for name, clause in modes.items():
        if _eval_clause(clause, vals):
            return name
    return "NEUTRAL"


def update_roles_and_flips(r: Registers, p: Personality) -> None:
    """Apply hysteresis flips and re-derive the persistent role (enhancement #1).

    Roles use asymmetric thresholds so Elaine is slow to forgive and doesn't flicker:
      nemesis: enter relationship <= -4 (or grudge), exit only at relationship >= 2
      favorite: enter relationship >= 6 and trust >= 6, exit only at relationship < 3
    """
    flips = p.flips or {}
    nem = flips.get("nemesis", {"enter": -4, "exit": 2})
    fav = flips.get("favorite", {"enter": 6, "exit": 3})

    if r.role == "nemesis":
        if r.relationship >= float(nem.get("exit", 2)) and not r.grudge:
            r.role = "stranger"
    elif r.role == "favorite":
        if r.relationship < float(fav.get("exit", 3)):
            r.role = "stranger"
    else:  # stranger
        if r.relationship <= float(nem.get("enter", -4)) or r.grudge:
            r.role = "nemesis"
        elif r.relationship >= float(fav.get("enter", 6)) and r.trust >= 6:
            r.role = "favorite"
