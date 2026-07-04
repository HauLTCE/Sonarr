"""Memory: a flat key/value slot store + turn counter + last-N input ring buffer.

Slots persist across turns. Set by transition actions (often from regex captures),
read by response templates. The turn counter is usable in `when:` guards and as the
RNG seed. The ring buffer is debug/affect-only — `when:` guards never see input text
(DESIGN §5).

Slot TTL (enhancement #5, 2026-06-30): a slot may carry an optional expiry expressed
in LOGICAL CLOCK ticks (turn counter), never wall-clock. A read at or past the expiry
tick treats the slot as unset. The return stack is added in Phase 5.
"""
from __future__ import annotations

from collections import deque
from dataclasses import dataclass, field
from typing import Any


@dataclass
class _Slot:
    value: Any
    expires_at: int | None = None  # logical turn at which this slot becomes unset


@dataclass
class Memory:
    turn: int = 0
    _slots: dict[str, _Slot] = field(default_factory=dict)
    _history: deque[str] = field(default_factory=lambda: deque(maxlen=8))
    # Return stack (Phase 5): only the return *position* is stacked; slots stay global.
    _stack: list[str] = field(default_factory=list)

    # --- slots ---

    def set(self, key: str, value: Any, ttl: int | None = None) -> None:
        """Set a slot. ttl (if given) is a number of logical turns until expiry."""
        expires_at = (self.turn + ttl) if ttl is not None else None
        self._slots[key] = _Slot(value, expires_at)

    def _live(self, key: str) -> _Slot | None:
        slot = self._slots.get(key)
        if slot is None:
            return None
        if slot.expires_at is not None and self.turn >= slot.expires_at:
            return None
        return slot

    def get(self, key: str, default: Any = None) -> Any:
        slot = self._live(key)
        return slot.value if slot is not None else default

    def has(self, key: str) -> bool:
        return self._live(key) is not None

    def clear(self, key: str) -> None:
        self._slots.pop(key, None)

    def slots(self) -> dict[str, Any]:
        """Live (non-expired) slots as a plain dict — for rendering/serialization."""
        return {k: s.value for k, s in self._slots.items() if self._live(k) is not None}

    def slot_expiry(self) -> dict[str, int]:
        """Absolute expiry turns for live slots that carry one.

        Persisted alongside slots() so a TTL slot (e.g. `warned` with ttl:5)
        keeps its expiry across a save/load cycle. Without this the expiry was
        dropped on serialization and the slot became permanent — which, since
        Discord saves/loads every message, meant TTL slots never expired at all.
        """
        return {k: s.expires_at for k, s in self._slots.items()
                if self._live(k) is not None and s.expires_at is not None}

    def apply_expiry(self, expiry: dict[str, int] | None) -> None:
        """Reattach absolute expiry turns to already-restored slots."""
        if not expiry:
            return
        for k, e in expiry.items():
            slot = self._slots.get(k)
            if slot is not None and e is not None:
                slot.expires_at = int(e)

    # --- turn / history ---

    def tick(self) -> None:
        """Advance the logical clock by one turn."""
        self.turn += 1

    def record_input(self, text: str) -> None:
        """Append normalized input to the debug/affect ring buffer (NOT readable by guards)."""
        self._history.append(text)

    def history(self) -> list[str]:
        return list(self._history)

    def repeat_count(self, text: str) -> int:
        """How many of the buffered inputs equal `text` (for repetition→affect, enhancement #4)."""
        return sum(1 for h in self._history if h == text)

    # --- return stack (Phase 5) ---

    def push_return(self, state: str) -> None:
        self._stack.append(state)

    def pop_return(self) -> str | None:
        return self._stack.pop() if self._stack else None

    def stack_depth(self) -> int:
        return len(self._stack)
