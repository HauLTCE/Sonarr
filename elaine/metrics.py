"""Session metrics — lightweight, deterministic counters over a conversation.

Records which intents fired, which modes were active, and which states were entered,
per AffectEngine session. Read-only telemetry: never influences a reply, never persisted,
no wall-clock. Exposed via the REPL `/metrics` command and the `coverage` CLI tool.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass, field


@dataclass
class Metrics:
    turns: int = 0
    intents: Counter = field(default_factory=Counter)
    modes: Counter = field(default_factory=Counter)
    states: Counter = field(default_factory=Counter)

    def record(self, intent_name: str | None, mode: str, state: str) -> None:
        self.turns += 1
        if intent_name:
            self.intents[intent_name] += 1
        if mode:
            self.modes[mode] += 1
        if state:
            self.states[state] += 1

    def summary(self) -> dict:
        return {
            "turns": self.turns,
            "intents": dict(self.intents.most_common()),
            "modes": dict(self.modes.most_common()),
            "states": dict(self.states.most_common()),
        }

    def as_lines(self) -> list[str]:
        out = [f"turns: {self.turns}"]
        if self.intents:
            top = ", ".join(f"{k}={v}" for k, v in self.intents.most_common(8))
            out.append(f"intents: {top}")
        if self.modes:
            out.append("modes: " + ", ".join(f"{k}={v}" for k, v in self.modes.most_common()))
        return out
