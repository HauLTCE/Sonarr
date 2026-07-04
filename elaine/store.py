"""SQLite persistence — the ONLY SQL surface (PERSISTENCE.md, Plan Phase 10).

Per-user and global Self survive restart. The engine never touches SQL; store.py
loads a fully-built Self and saves the mutated one. Logical clock only — no wall-clock
on any output path. Catch-up decay on load applies the elapsed logical gap (capped).
"""
from __future__ import annotations

import json
import sqlite3
import threading
from dataclasses import dataclass, field

from .affect import Personality, Registers, decay_step
from .episodic import EpisodicLog, Quote
from .self_model import GlobalState, Self

_SCHEMA = """
CREATE TABLE IF NOT EXISTS user_state (
    user_key       TEXT PRIMARY KEY,
    dialogue_state TEXT NOT NULL,
    registers      TEXT NOT NULL,
    slots          TEXT NOT NULL,
    logical_clock  INTEGER NOT NULL,
    updated_clock  INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS episodic (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    user_key  TEXT NOT NULL,
    turn      INTEGER NOT NULL,
    text      TEXT NOT NULL,
    tag       TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_episodic_user_tag ON episodic(user_key, tag);
CREATE TABLE IF NOT EXISTS global_state (
    guild_id     TEXT PRIMARY KEY,
    global_mood  REAL NOT NULL,
    global_clock INTEGER NOT NULL
);
"""


@dataclass
class LoadedSelf:
    self_state: Self
    slots: dict
    logical_clock: int
    slot_expiry: dict = field(default_factory=dict)  # {slot_key: absolute expiry turn}
    global_mood: float = 0.0  # per-guild room mood at load time


class Store:
    def __init__(self, path: str = ":memory:", personality: Personality | None = None,
                 episodic_cap: int = 50, decay_cap: int = 50, start_state: str = "GREET"):
        self.path = path
        self.personality = personality or Personality()
        self.episodic_cap = episodic_cap
        self.decay_cap = decay_cap
        self.start_state = start_state
        self._conn = sqlite3.connect(path, check_same_thread=False)
        self._conn.executescript(_SCHEMA)
        self._conn.commit()
        self._locks: dict[str, threading.Lock] = {}
        self._locks_guard = threading.Lock()
        # Serializes ALL access to the single shared connection. The per-user
        # lock (lock_for) only orders one user's own messages; it does NOT stop
        # two different users' threads from hitting the same connection at once,
        # which interleaves save()'s multi-statement transaction and corrupts
        # state. Reentrant so a method that calls another (save -> global_clock)
        # doesn't self-deadlock. The engine step runs outside this lock, so
        # cross-user CPU work still parallelizes; only the DB touchpoints serialize.
        self._db_lock = threading.RLock()

    def lock_for(self, user_key: str) -> threading.Lock:
        """Per-user-key lock so two messages from one user don't race their Self."""
        with self._locks_guard:
            if user_key not in self._locks:
                self._locks[user_key] = threading.Lock()
            return self._locks[user_key]

    def global_clock(self, guild_id: str) -> int:
        with self._db_lock:
            row = self._conn.execute(
                "SELECT global_clock FROM global_state WHERE guild_id=?", (guild_id,)
            ).fetchone()
            return row[0] if row else 0

    def global_mood(self, guild_id: str) -> float:
        """Per-guild room mood (shared across that guild's users)."""
        with self._db_lock:
            row = self._conn.execute(
                "SELECT global_mood FROM global_state WHERE guild_id=?", (guild_id,)
            ).fetchone()
            return float(row[0]) if row else 0.0

    def load(self, user_key: str, guild_id: str = "default") -> LoadedSelf:
        """Load a user's Self, applying catch-up decay for the elapsed logical gap."""
        with self._db_lock:
            cur_global = self.global_clock(guild_id)
            cur_mood = self.global_mood(guild_id)
            row = self._conn.execute(
                "SELECT dialogue_state, registers, slots, logical_clock, updated_clock "
                "FROM user_state WHERE user_key=?", (user_key,)
            ).fetchone()

            if row is None:
                # first contact: build fresh from baselines
                regs = Registers.fresh(self.personality)
                self_state = Self(registers=regs, log=EpisodicLog(cap=self.episodic_cap),
                                  dialogue_state=self.start_state)
                return LoadedSelf(self_state=self_state, slots={}, logical_clock=0,
                                  slot_expiry={}, global_mood=cur_mood)

            dialogue_state, regs_json, slots_json, logical_clock, updated_clock = row
            regs = Registers.from_dict(json.loads(regs_json))
            slots, slot_expiry = self._decode_slots(json.loads(slots_json))

            # catch-up decay: elapsed logical gap, capped
            gap = max(0, cur_global - updated_clock)
            if gap:
                decay_step(regs, self.personality, steps=min(gap, self.decay_cap))

            # episodic
            qrows = self._conn.execute(
                "SELECT turn, text, tag FROM episodic WHERE user_key=? ORDER BY id", (user_key,)
            ).fetchall()
            log = EpisodicLog(cap=self.episodic_cap)
            log.quotes = [Quote(t, x, g) for (t, x, g) in qrows]

            self_state = Self(registers=regs, log=log, dialogue_state=dialogue_state)
            return LoadedSelf(self_state=self_state, slots=slots, logical_clock=logical_clock,
                              slot_expiry=slot_expiry, global_mood=cur_mood)

    @staticmethod
    def _decode_slots(raw) -> tuple[dict, dict]:
        """Decode the persisted slots blob into (values, expiry).

        Format v2 is a versioned envelope {"__slotfmt__":2, "values":..., "expiry":...}
        that preserves per-slot TTLs. Anything else is treated as the legacy plain
        {key: value} dict with no expiry info (backward compatible).
        """
        if isinstance(raw, dict) and raw.get("__slotfmt__") == 2:
            return (raw.get("values") or {}), (raw.get("expiry") or {})
        return (raw or {}), {}

    def save(self, user_key: str, self_state: Self, slots: dict, logical_clock: int,
             guild_id: str = "default", slot_expiry: dict | None = None,
             global_mood: float = 0.0) -> None:
        """Persist the full Self in one transaction; bump global clock; prune episodic."""
        slots_blob = json.dumps({"__slotfmt__": 2, "values": slots, "expiry": slot_expiry or {}})
        with self._db_lock, self._conn:  # DB-wide lock + transaction
            cur = self._conn
            new_global = self.global_clock(guild_id) + 1
            # Persist the room mood too (the upsert previously only touched
            # global_clock and hardcoded mood to 0.0, so global_mood was dead).
            cur.execute(
                "INSERT INTO global_state(guild_id, global_mood, global_clock) VALUES(?,?,?) "
                "ON CONFLICT(guild_id) DO UPDATE SET "
                "global_mood=excluded.global_mood, global_clock=excluded.global_clock",
                (guild_id, float(global_mood), new_global),
            )
            cur.execute(
                "INSERT INTO user_state(user_key, dialogue_state, registers, slots, logical_clock, updated_clock) "
                "VALUES(?,?,?,?,?,?) ON CONFLICT(user_key) DO UPDATE SET "
                "dialogue_state=excluded.dialogue_state, registers=excluded.registers, "
                "slots=excluded.slots, logical_clock=excluded.logical_clock, updated_clock=excluded.updated_clock",
                (user_key, self_state.dialogue_state, json.dumps(self_state.registers.to_dict()),
                 slots_blob, logical_clock, new_global),
            )
            # rewrite episodic (simple + correct; cap-bounded so cheap)
            cur.execute("DELETE FROM episodic WHERE user_key=?", (user_key,))
            quotes = self_state.log.quotes[-self.episodic_cap :]
            cur.executemany(
                "INSERT INTO episodic(user_key, turn, text, tag) VALUES(?,?,?,?)",
                [(user_key, q.turn, q.text, q.tag) for q in quotes],
            )

    def reset(self, user_key: str) -> None:
        with self._db_lock, self._conn:
            self._conn.execute("DELETE FROM user_state WHERE user_key=?", (user_key,))
            self._conn.execute("DELETE FROM episodic WHERE user_key=?", (user_key,))

    def users(self) -> list[dict]:
        """List stored users with a small summary row each (for the `users` CLI command)."""
        with self._db_lock:
            rows = self._conn.execute(
                "SELECT user_key, dialogue_state, logical_clock, updated_clock FROM user_state "
                "ORDER BY user_key"
            ).fetchall()
        return [
            {"user_key": k, "dialogue_state": ds, "logical_clock": lc, "updated_clock": uc}
            for (k, ds, lc, uc) in rows
        ]

    def close(self) -> None:
        with self._db_lock:
            self._conn.close()
