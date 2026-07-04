"""Replay test runner (Plan Phase 6).

A fixture is a plain-text transcript:
  - lines starting with '> ' are user input
  - lines starting with '< ' are expected bot output
  - the first run of '<' lines, before any '>', is the bootstrap greeting
  - consecutive '<' lines join with '\\n' and compare against one turn's full output

Determinism (turn-seeded RNG, no wall-clock) is what makes exact-match viable.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from .engine import Engine
from .script import load


@dataclass
class ReplayStep:
    user: str
    expected: str


@dataclass
class Replay:
    greeting: str | None
    steps: list[ReplayStep] = field(default_factory=list)


def parse_replay(text: str) -> Replay:
    greeting_lines: list[str] = []
    steps: list[ReplayStep] = []
    pending_user: str | None = None
    pending_out: list[str] = []
    seen_input = False

    def flush():
        nonlocal pending_user, pending_out
        if pending_user is not None:
            steps.append(ReplayStep(pending_user, "\n".join(pending_out)))
        pending_user = None
        pending_out = []

    for raw in text.splitlines():
        if raw.startswith(">"):
            flush()
            pending_user = raw[1:].lstrip()
            seen_input = True
            pending_out = []
        elif raw.startswith("<"):
            content = raw[1:]
            if content.startswith(" "):
                content = content[1:]
            if not seen_input:
                greeting_lines.append(content)
            else:
                pending_out.append(content)
        # blank/comment lines ignored
    flush()
    greeting = "\n".join(greeting_lines) if greeting_lines else None
    return Replay(greeting=greeting, steps=steps)


def run_replay(script_path: str | Path, fixture_text: str, affect: bool = False) -> list[str]:
    """Replay a fixture; raise AssertionError on the first mismatch. Returns all replies.

    affect=False uses the bare symbolic Engine (plain §5.1 rendering — fixtures must avoid
    #grammar# symbols). affect=True drives the full AffectEngine so grammar + mode + affect
    expansion is replayed; determinism still holds (complete-Self snapshot, clock-seeded RNG).
    """
    loaded = load(script_path)
    if affect:
        from .self_model import AffectEngine
        engine = AffectEngine(loaded)
    else:
        engine = Engine(loaded.states, loaded.start)
    replies: list[str] = []

    replay = parse_replay(fixture_text)
    if replay.greeting is not None:
        actual = engine.greeting()
        assert actual == replay.greeting, (
            f"greeting mismatch:\n  expected: {replay.greeting!r}\n  actual:   {actual!r}"
        )

    for i, step in enumerate(replay.steps):
        result = engine.step(step.user)
        replies.append(result.reply)
        assert result.reply == step.expected, (
            f"turn {i} (input {step.user!r}) mismatch:\n"
            f"  expected: {step.expected!r}\n  actual:   {result.reply!r}"
        )
    return replies
