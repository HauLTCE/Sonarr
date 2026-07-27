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

import logging
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

logger = logging.getLogger(__name__)


def _regs_snapshot(r: "Registers") -> dict[str, float]:
    """Copy the continuous mood axes so we can log before/after deltas for one turn."""
    return {
        "anger": r.anger, "warmth": r.warmth, "amusement": r.amusement,
        "boredom": r.boredom, "confidence": r.confidence, "energy": r.energy,
    }


def _delta(before: float, after: float) -> str:
    """Render a register as 'x.y' if unchanged this turn, else 'before->after'."""
    if abs(before - after) < 1e-9:
        return f"{after:.1f}"
    return f"{before:.1f}->{after:.1f}"


def _short(text, limit: int = 140) -> str:
    """One-line, length-bounded form of a value for readable log lines (ASCII-safe)."""
    s = text if isinstance(text, str) else ("" if text is None else str(text))
    s = s.replace("\n", " ").replace("\r", " ")
    return s if len(s) <= limit else s[: limit - 3] + "..."


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
        mode_grammar = state.mode_grammar if state else {}
        text = expand_template(template, baseline, mode_grammar, mode,
                               self.memory, captures, ctx, clock, salt)
        # Anti-repeat: finite pools + per-turn RNG will otherwise pick the SAME short line on
        # adjacent turns ("noted." / "noted." / "noted."), which reads broken when a user
        # hammers one input like "ok". bot_last_reply holds the PREVIOUS turn's emitted reply
        # during this render (it's rewritten post-step), so an exact match means an immediate
        # repeat — deterministically re-roll the pick a few times. No-op when the template has
        # no variety (a fixed line just re-renders identically and we keep it) or on turn 1.
        last = self.memory.get("bot_last_reply")
        if text and last and text == last:
            for bump in (0x5BD1E995, 0x27D4EB2F, 0x165667B1):
                alt = expand_template(template, baseline, mode_grammar, mode,
                                      self.memory, captures, ctx, clock, salt ^ bump)
                if alt and alt != last:
                    return alt
        return text

    def greeting(self) -> str:
        return self.engine.greeting()

    def current_mode(self) -> str:
        return evaluate_mode(self.self_state.registers, self.modes,
                             self.global_state.global_mood, self.personality)

    def step(self, user_text: str) -> StepResult:
        r = self.self_state.registers
        prev_state = self.engine.current
        debug = logger.isEnabledFor(logging.DEBUG)
        before = _regs_snapshot(r) if debug else None
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

        # 5b. self-consistency (Phases 1 & 4): the engine has no memory of its OWN outputs,
        # so a reply could contradict the previous turn — famously "i don't remember asking."
        # right after she asked. Record a tiny, persisted trace of what she just said/did so
        # the NEXT turn's guards can stay coherent. Must run on the FINAL (post-cooldown)
        # result and after the mode is settled.
        self._record_self_act(result)

        self._record_metrics()
        self.global_state.global_clock += 1

        # ---- observability (Phase: better chat logging) --------------------
        # One INFO line tells the whole turn's story: what came in, how it was read,
        # where the FSM moved, the active mode, which matcher fired, and what went out.
        # DEBUG adds the per-register affect deltas + relational state, which is what
        # you actually need when a reply or a mood looks wrong.
        logger.info(
            "turn=%d %s->%s sentiment=%s mode=%s fired=%s | in=%r out=%r",
            self.memory.turn, prev_state, self.engine.current, label,
            self.self_state.last_mode, self._fired_label(),
            _short(user_text), _short(result.reply),
        )
        if before is not None:
            logger.debug(
                "  affect: anger %s warmth %s amus %s bore %s conf %s energy %s | "
                "rel=%.1f trust=%.1f role=%s grudge=%s insults=%d | room_mood=%.2f reps=%d",
                _delta(before["anger"], r.anger), _delta(before["warmth"], r.warmth),
                _delta(before["amusement"], r.amusement), _delta(before["boredom"], r.boredom),
                _delta(before["confidence"], r.confidence), _delta(before["energy"], r.energy),
                r.relationship, r.trust, r.role, r.grudge, r.times_insulted,
                self.global_state.global_mood, reps,
            )
        return result

    def _fired_label(self) -> str:
        """Human-readable id of the transition that fired this turn (for logs/metrics)."""
        from .matchers import Intent
        t = getattr(self.engine, "_last_transition", None)
        if t is None:
            return "none"
        if t.matcher is None:
            return "always/fallback"
        if isinstance(t.matcher, Intent):
            return f"intent:{t.matcher.name}"
        return type(t.matcher).__name__

    def _record_self_act(self, result: StepResult) -> None:
        """Persist Elaine's own last speech act so the next turn stays self-consistent.

        Writes three slots (slots, not registers, so they persist AND expire via TTL):
          bot_asked     — did this reply pose a question? (explicit act:question OR a
                          '?'-terminated reply). This is what stops "i don't remember
                          asking." from firing right after she asked.
          bot_last_act  — the declared `act:` tag, else a light heuristic.
          bot_last_reply— the emitted text (bounded); handy for logs / echo-avoidance.
        TTL is 2 logical turns for the boolean (live for exactly the next turn, then gone)
        and 3 for the descriptive slots. Slots persist across Discord messages via store.py.
        """
        t = getattr(self.engine, "_last_transition", None)
        act = getattr(t, "act", None) if t is not None else None
        reply = result.reply or ""
        ended_q = reply.rstrip().endswith("?")
        if act is None:
            act = "question" if ended_q else self._fired_act_hint()
        asked = ended_q or act == "question"
        self.memory.set("bot_asked", bool(asked), ttl=2)
        self.memory.set("bot_last_act", act, ttl=3)
        if reply:
            self.memory.set("bot_last_reply", _short(reply, 200), ttl=3)

    def _fired_act_hint(self) -> str:
        """Best-effort speech act when the fired transition declares no explicit `act:`.

        Deliberately conservative: only the cases we can read off the matcher cheaply.
        Everything else is a plain 'statement'. Explicit `act:` tags in the script always
        win over this (see _record_self_act).
        """
        from .matchers import Intent
        t = getattr(self.engine, "_last_transition", None)
        if t is not None and isinstance(t.matcher, Intent):
            if t.matcher.name == "GREETING":
                return "greeting"
            if t.matcher.name == "BYE":
                return "farewell"
        return "statement"

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
                logger.info("cooldown: RELEASE after %d msg(s) -> CHAT (anger now %.1f)",
                            since, r.anger)
                return StepResult(reply=text, halted=False)
            logger.debug("cooldown: holding (%d/%d msg(s), anger=%.1f)", since, duration, r.anger)
            return result

        # Enter cooldown when anger crosses the threshold (and we're not terminal).
        if r.anger >= enter and not result.halted:
            self.engine.current = cooldown_state
            self._set_cooldown_since(self.memory.turn)
            state = self.loaded.states[cooldown_state]
            text = self._render(state.on_enter, {}, cooldown_state, 0) if state.on_enter else ""
            logger.info("cooldown: ENTER at anger=%.1f (>= %.1f); holding %d msg(s)",
                        r.anger, enter, duration)
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
