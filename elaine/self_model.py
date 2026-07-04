"""The complete Self and the affect-aware turn pipeline (AFFECT_MODEL §1, Plan Phase 9).

Self = (dialogue_state, Registers, EpisodicLog). This module wraps the symbolic Engine
with the affect layer so one turn does, in order (GRAMMAR §5):
  1. classify sentiment (symbolic recognizer) -> your_sentiment register
  2. repetition->affect nudges (enhancement #4, from the ring buffer)
  3. decay toward baselines (logical clock)
  4. engine.step() runs the FSM; transition affect: deltas + remember: applied
  5. momentum + role/flip update; mode recomputed
  6. if the state declares grammar, render the reply through the grammar in the active mode

The engine stays affect-ignorant; this layer reads/writes Registers around it.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from . import sentiment as S
from .affect import (
    Personality,
    Registers,
    decay_step,
    effective_anger,
    evaluate_mode,
    momentum_update,
    update_roles_and_flips,
)
from .engine import Engine, StepResult
from .episodic import AffectFetchContext, EpisodicLog
from .grammar import expand_entry, expand_template
from .metrics import Metrics
from .normalize import normalize


@dataclass
class GlobalState:
    global_mood: float = 0.0
    global_clock: int = 0


@dataclass
class Self:
    registers: Registers
    log: EpisodicLog
    dialogue_state: str
    last_mode: str = "NEUTRAL"


class AffectEngine:
    """Engine + affect. Holds one user's Self; orchestrates the per-turn pipeline."""

    def __init__(self, loaded, personality: Personality | None = None,
                 self_state: Self | None = None, global_state: GlobalState | None = None):
        self.loaded = loaded
        self.personality = personality or Personality.from_config(loaded.personality)
        self.modes = loaded.modes or {"NEUTRAL": {"always": True}}
        self.global_state = global_state or GlobalState()

        if self_state is None:
            regs = Registers.fresh(self.personality)
            self_state = Self(registers=regs, log=EpisodicLog(), dialogue_state=loaded.start)
        self.self_state = self_state

        self.engine = Engine(loaded.states, loaded.start, renderer=self._render)
        self.engine.current = self_state.dialogue_state
        self.metrics = Metrics()  # session-only telemetry (never persisted, never read back)

    # Slot key holding the turn at which COOLDOWN was entered. It MUST live in
    # memory (persisted) rather than on the engine instance: a fresh AffectEngine
    # is built for every Discord message, so an instance attribute reset to 0
    # each turn — making `since` huge and releasing COOLDOWN on the very next
    # message (duration_msgs was silently ignored). The double underscores keep
    # it from colliding with any script-defined slot.
    _COOLDOWN_SLOT = "__cooldown_since__"

    def _get_cooldown_since(self) -> int:
        val = self.memory.get(self._COOLDOWN_SLOT)
        return int(val) if isinstance(val, (int, float)) else 0

    def _set_cooldown_since(self, turn: int) -> None:
        self.memory.set(self._COOLDOWN_SLOT, turn)

    @property
    def memory(self):
        return self.engine.memory

    @property
    def current(self) -> str:
        """Current dialogue state — mirrors Engine.current so callers (CLI) are uniform."""
        return self.engine.current

    def _render(self, template: str, captures: dict, state_name: str, salt: int = 0) -> str:
        """Grammar-aware renderer injected into the engine.

        Expands #symbol# in reply:/on_enter: against the owning state's grammar MERGED with
        the shared global pools (imported response banks), in the CURRENT mode (computed
        live, after this turn's sentiment/affect force). Falls back to plain §5.1 only when
        there's no grammar at all and no #symbol# to expand.
        """
        from .responses import render as plain_render

        state = self.loaded.states.get(state_name)
        clock = self.memory.turn
        global_grammar = getattr(self.loaded, "global_grammar", {}) or {}
        local = (state.grammar if state else {}) or {}
        # Global pools are a backstop; a state's own symbol wins over a pool of the same name.
        baseline = {**global_grammar, **local}

        if not baseline and "#" not in template:
            return plain_render(template, self.memory, captures, clock, salt)

        mode = self.current_mode()
        ctx = AffectFetchContext(
            log=self.self_state.log,
            name_slot=self.memory.get("name"),
            mode_name=mode,
            counts={"times_insulted": self.self_state.registers.times_insulted},
            turn=clock,
            facts=self.memory.get("facts") or {},
            topic=self.memory.get("topic"),
            daypart=self.memory.get("time_of_day"),
            topics_seen=self.memory.get("topics_seen") or [],
        )
        return expand_template(template, baseline, (state.mode_grammar if state else {}),
                               mode, self.memory, captures, ctx, clock, salt)

    def greeting(self) -> str:
        return self.engine.greeting()

    def current_mode(self) -> str:
        return evaluate_mode(self.self_state.registers, self.modes,
                             self.global_state.global_mood, self.personality)

    def step(self, user_text: str) -> StepResult:
        r = self.self_state.registers
        norm = normalize(user_text)

        # 1. sentiment classification -> register
        label = S.classify(user_text)
        r.your_sentiment = label
        if label == S.HOSTILE:
            r.times_insulted += 1
        # Expose the label to the FSM as a slot so transitions can guard on it
        # (when: {equals: [sentiment, HOSTILE]}). This lets the graph react to crude
        # trash-talk that no specific jab-word regex would catch.
        self.memory.set("sentiment", label)

        # 1b. room mood: nudge the shared per-guild mood by this message's sentiment,
        # with light decay toward neutral. Downstream only a NEGATIVE mood matters
        # (effective_anger bleeds from a hostile room), so keep magnitudes small.
        # This is what makes the room-mood feature actually contribute — previously
        # global_mood was permanently 0 in Discord.
        gm = self.global_state.global_mood * 0.9
        if label == S.HOSTILE:
            gm -= 1.0
        elif label == S.FRIENDLY:
            gm += 0.5
        self.global_state.global_mood = max(-10.0, min(10.0, gm))

        # 2. decay toward baseline FIRST, then apply this turn's forces. (Applying forces
        # before decay let decay immediately eat them, so boredom/anger could never cross
        # their mode thresholds — that's why spamming felt like it did nothing.)
        decay_step(r, self.personality, steps=1)

        # 3. repetition -> affect (enhancement #4): exact/near-repeat spikes boredom, and
        # sustained spam stokes anger. Magnitudes are tuned to actually clear BORED (>=6)
        # and ANNOYED (>=4) against the decay rates, not just asymptote below them.
        reps = self.memory.repeat_count(norm.lower)
        if norm.lower and reps >= 1:
            r.boredom = min(10.0, r.boredom + 2.8)
            if reps >= 2:
                r.anger = min(10.0, r.anger + 1.5)

        # 4. sentiment force on the relational/mood axes
        self._apply_sentiment_force(label)

        # 5. run the FSM
        result = self.engine.step(user_text)
        self._apply_transition_affect()

        # 4b. affect-gated COOLDOWN (AFFECT_MODEL §7): at extreme anger Elaine refuses to
        # engage for a few messages. This is the one case where a register threshold
        # legitimately overrides the dialogue graph. Never a moderation action — text only.
        result = self._maybe_cooldown(result)

        # 5. momentum + roles + mode
        momentum_update(r)
        update_roles_and_flips(r, self.personality)
        self.self_state.dialogue_state = self.engine.current
        self.self_state.last_mode = self.current_mode()

        self._record_metrics()
        self.global_state.global_clock += 1
        return result

    def _record_metrics(self) -> None:
        from .matchers import Intent
        t = getattr(self.engine, "_last_transition", None)
        intent = t.matcher.name if (t is not None and isinstance(t.matcher, Intent)) else None
        self.metrics.record(intent, self.self_state.last_mode, self.engine.current)

    def _maybe_cooldown(self, result: StepResult) -> StepResult:
        cooldown_state = "COOLDOWN"
        if cooldown_state not in self.loaded.states:
            return result
        if cooldown_state not in (self.loaded.affect_states or []):
            return result
        r = self.self_state.registers
        enter = self.personality.cooldown_enter_anger
        duration = max(1, self.personality.cooldown_duration)

        # Already cooling: release after duration_msgs messages have elapsed. (Without this
        # she was trapped in COOLDOWN forever — only "sorry" escaped — because the
        # duration_msgs config was never honored. We release on the message counter, not on
        # anger decay, so the dwell time is predictable and matches the config.)
        if self.engine.current == cooldown_state:
            since = self.memory.turn - self._get_cooldown_since()
            if since >= duration:
                self.engine.current = "CHAT"
                self.memory.clear(self._COOLDOWN_SLOT)
                # bleed off the heat so she doesn't snap straight back into cooldown
                r.anger = min(r.anger, enter - 2.0)
                chat = self.loaded.states.get("CHAT")
                text = self._render(chat.on_enter, {}, "CHAT", 0) if chat and chat.on_enter else ""
                return StepResult(reply=text, halted=False)
            return result

        # Enter cooldown when anger crosses the threshold (and we're not terminal).
        if r.anger >= enter and not result.halted:
            self.engine.current = cooldown_state
            self._set_cooldown_since(self.memory.turn)
            state = self.loaded.states[cooldown_state]
            text = self._render(state.on_enter, {}, cooldown_state, 0) if state.on_enter else ""
            return StepResult(reply=text, halted=False)
        return result

    def _apply_sentiment_force(self, label: str) -> None:
        r = self.self_state.registers
        if label == S.HOSTILE:
            r.anger = min(10.0, r.anger + 2.0)
            r.relationship = max(-10.0, r.relationship - 1.0)
            r.confidence = max(0.0, r.confidence - 0.5)
            if r.anger >= 8:
                r.grudge = True
        elif label == S.FRIENDLY:
            r.warmth = min(10.0, r.warmth + 1.5)
            r.relationship = min(10.0, r.relationship + 1.0)
            r.trust = min(10.0, r.trust + 0.3)

    def _apply_transition_affect(self) -> None:
        """Apply the just-fired transition's affect: deltas and remember: quote."""
        t = getattr(self.engine, "_last_transition", None)
        mr = getattr(self.engine, "_last_match", None)
        if t is None:
            return
        r = self.self_state.registers
        for reg, delta in t.affect:
            if hasattr(r, reg):
                cur = getattr(r, reg)
                setattr(r, reg, max(-10.0, min(10.0, cur + delta)))
        if t.remember is not None and mr is not None:
            name = t.remember[1:]
            if name in mr.captures:
                self.self_state.log.add(self.memory.turn, mr.captures[name], r.your_sentiment)

    def render_grammar_reply(self, entry: str = "reply") -> str | None:
        """If the current state declares grammar, expand entry in the active mode."""
        state = self.loaded.states[self.engine.current]
        if not state.grammar:
            return None
        mode = self.self_state.last_mode
        ctx = AffectFetchContext(
            log=self.self_state.log,
            name_slot=self.memory.get("name"),
            mode_name=mode,
            counts={"times_insulted": self.self_state.registers.times_insulted},
            turn=self.memory.turn,
        )
        return expand_entry(state.grammar, state.mode_grammar, mode, entry,
                            self.memory, {}, ctx, self.memory.turn)
