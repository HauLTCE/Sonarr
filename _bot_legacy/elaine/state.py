"""State / Transition data model (DESIGN §3, Plan Phase 2).

Transitions evaluate in DECLARED ORDER — position in the list is priority, there is no
separate priority field. The first transition whose full guard holds wins. The full
guard is `matcher(input) AND when(memory)` — both must hold (when is optional).

A transition's action verbs are mutually exclusive: at most one of goto / push / pop
(push/pop added in Phase 5). Validation enforces this.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .guards import Guard
from .matchers import Matcher


@dataclass(frozen=True)
class SetAction:
    """One slot mutation. `value` is a capture-ref ($x/$0) or a literal scalar.

    op selects the mutation:
      set     assign value (default)
      inc/dec add/subtract a numeric amount (counters)
      append  push value onto a list slot
      clear   unset the slot
      rand    store a deterministic random int; value is [lo, hi]
      pick    store a deterministic choice from value (a list of literals)
    ttl: optional logical-turn lifetime (enhancement #5). None = never expires.
    """

    key: str
    value: Any
    is_capture_ref: bool
    ttl: int | None = None
    op: str = "set"


@dataclass(frozen=True)
class Transition:
    matcher: Matcher | None  # None = always (input side unconditional)
    when: Guard | None = None
    sets: tuple[SetAction, ...] = ()
    goto: str | None = None
    push: str | None = None
    pop: bool = False
    reply: str | None = None
    # Phase 9 affect verbs:
    affect: tuple[tuple[str, float], ...] = ()   # register deltas, e.g. (("anger", 2.0),)
    remember: str | None = None                  # capture ref to log as a quote ($0/$x)
    # context / scheduling verbs:
    topic: str | None = None                     # sets the current conversation topic on fire
    cooldown: int = 0                            # won't re-fire for this many logical turns
    once: bool = False                           # fire at most once per session
    key: str = ""                                # stable id (state#index) for cooldown/once
    # Phase 4 (speech acts): a declarative label for what THIS reply DOES conversationally
    # (question / greeting / dismissal / farewell / ...). Recorded each turn so the NEXT
    # turn can stay self-consistent — e.g. never deny asking right after asking. When None
    # the affect layer infers a light heuristic act instead. Pure metadata: no control flow.
    act: str | None = None

    def changes_state(self) -> bool:
        """True if this transition moves to a different node (fires target on_enter)."""
        return self.goto is not None or self.push is not None or self.pop


@dataclass(frozen=True)
class State:
    name: str
    on_enter: str | None = None
    transitions: tuple[Transition, ...] = ()
    fallback: Transition | None = None
    end: bool = False
    # Grammar (Phase 8) attached later; kept here so State stays the single node type.
    grammar: dict[str, Any] = field(default_factory=dict)
    mode_grammar: dict[str, Any] = field(default_factory=dict)
