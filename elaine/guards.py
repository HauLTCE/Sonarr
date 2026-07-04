"""The `when:` guard predicate language (DESIGN §3, §5.1).

A small, fixed, pure predicate over memory (slots + turn counter) plus — for numeric
compares only — this turn's regex captures. has(slot), equals(slot, literal), numeric
slot/capture compares, regex/substring/membership over a slot, not/all/any combinators,
turn compares. Never runs arbitrary code, never reads the network.

`test(memory, captures=None)`: captures is this turn's match groups, so a guard can
compare a just-captured value to a stored slot (e.g. slot_gt: [$number, secret]) — which
is what makes single-turn number-guessing possible. Most guards ignore captures.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Protocol

from .memory import Memory


def canonical(value: Any) -> str:
    """Canonical string form for equals comparison (DESIGN §5.1)."""
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        if value.is_integer():
            return str(int(value))
        return repr(value)
    return str(value)


def _as_float(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def _operand(a) -> tuple:
    """Classify a slot_cmp operand: number -> literal, $x -> capture, else -> slot."""
    if isinstance(a, bool):
        return ("literal", 1.0 if a else 0.0)
    if isinstance(a, (int, float)):
        return ("literal", float(a))
    if isinstance(a, str):
        if a.startswith("$"):
            return ("capture", a[1:])
        try:
            return ("literal", float(a))
        except ValueError:
            return ("slot", a)
    raise ValueError(f"bad slot_cmp operand {a!r}")


def _resolve_num(operand: tuple, memory: Memory, captures: dict | None):
    kind, value = operand
    if kind == "literal":
        return value
    if kind == "slot":
        return _as_float(memory.get(value)) if memory.has(value) else None
    if kind == "capture":
        v = (captures or {}).get(value)
        return _as_float(v) if v is not None else None
    return None


class Guard(Protocol):
    def test(self, memory: Memory, captures: dict | None = None) -> bool: ...


@dataclass(frozen=True)
class Has:
    slot: str

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return memory.has(self.slot)


@dataclass(frozen=True)
class Equals:
    slot: str
    literal: Any

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        if not memory.has(self.slot):
            return False
        return canonical(memory.get(self.slot)) == canonical(self.literal)


@dataclass(frozen=True)
class Not:
    inner: Guard

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return not self.inner.test(memory, captures)


@dataclass(frozen=True)
class TurnLt:
    n: int

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return memory.turn < self.n


@dataclass(frozen=True)
class TurnGe:
    n: int

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return memory.turn >= self.n


@dataclass(frozen=True)
class SlotCmp:
    """Numeric compare (gt/ge/lt/le) of two operands, each a slot, $capture, or literal.

    slot_gt: [n, 5]            slot n > 5
    slot_gt: [guess, secret]   slot guess > slot secret     (across turns)
    slot_gt: [$number, secret] this turn's capture > secret (single turn — guessing games)
    False if either operand is missing or non-numeric.
    """

    left: tuple
    op: str
    right: tuple

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        a = _resolve_num(self.left, memory, captures)
        b = _resolve_num(self.right, memory, captures)
        if a is None or b is None:
            return False
        return {"gt": a > b, "ge": a >= b, "lt": a < b, "le": a <= b}[self.op]


@dataclass(frozen=True)
class Matches:
    """A slot's string value matches a regex (re.search). Compiled once at load."""

    slot: str
    pattern: "re.Pattern"

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        if not memory.has(self.slot):
            return False
        return bool(self.pattern.search(str(memory.get(self.slot))))


@dataclass(frozen=True)
class Contains:
    """A slot's string value contains a substring (case-insensitive)."""

    slot: str
    needle: str

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        if not memory.has(self.slot):
            return False
        return self.needle.lower() in str(memory.get(self.slot)).lower()


@dataclass(frozen=True)
class SlotIn:
    """A slot's canonical value is one of a set of options."""

    slot: str
    options: tuple

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        if not memory.has(self.slot):
            return False
        return canonical(memory.get(self.slot)) in {canonical(o) for o in self.options}


@dataclass(frozen=True)
class All:
    parts: tuple[Guard, ...]

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return all(p.test(memory, captures) for p in self.parts)


@dataclass(frozen=True)
class Any_:
    parts: tuple[Guard, ...]

    def test(self, memory: Memory, captures: dict | None = None) -> bool:
        return any(p.test(memory, captures) for p in self.parts)
