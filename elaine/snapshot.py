"""Serialize / restore a live AffectEngine's Self to a plain dict (JSON-friendly).

Powers the REPL `/save` and `/load` commands and the `export-self` CLI subcommand.
This is the in-memory sibling of store.py (which owns the SQLite surface): same Self
shape, no database. Deterministic; a restored engine replays identically from that point.
"""
from __future__ import annotations

from .affect import Registers
from .episodic import EpisodicLog
from .memory import Memory

SNAPSHOT_VERSION = 1


def dump_engine(engine) -> dict:
    """Capture an AffectEngine's complete Self (registers, log, state, slots, clock)."""
    ss = engine.self_state
    return {
        "version": SNAPSHOT_VERSION,
        "dialogue_state": ss.dialogue_state,
        "last_mode": ss.last_mode,
        "registers": ss.registers.to_dict(),
        "log": ss.log.to_list(),
        "slots": engine.memory.slots(),
        "turn": engine.memory.turn,
        "global": {
            "global_mood": engine.global_state.global_mood,
            "global_clock": engine.global_state.global_clock,
        },
    }


def load_engine_state(engine, data: dict) -> None:
    """Restore a snapshot into an existing AffectEngine, in place."""
    if data.get("version") != SNAPSHOT_VERSION:
        raise ValueError(f"unsupported snapshot version {data.get('version')!r}")
    ss = engine.self_state
    ss.registers = Registers.from_dict(data.get("registers", {}))
    ss.log = EpisodicLog.from_list(data.get("log", []))
    ss.dialogue_state = data.get("dialogue_state", engine.loaded.start)
    ss.last_mode = data.get("last_mode", "NEUTRAL")

    mem = Memory()
    mem.turn = int(data.get("turn", 0))
    for k, v in (data.get("slots") or {}).items():
        mem.set(k, v)
    engine.engine.memory = mem
    engine.engine.current = ss.dialogue_state

    g = data.get("global") or {}
    engine.global_state.global_mood = float(g.get("global_mood", 0.0))
    engine.global_state.global_clock = int(g.get("global_clock", 0))
