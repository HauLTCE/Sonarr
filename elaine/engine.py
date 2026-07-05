"""The engine — step() is the heart of the machine (DESIGN §3, Plan Phase 3).

One turn = normalize -> match (declared order, first whose matcher AND when hold) ->
select (or fallback) -> act (write captures + sets) -> move -> render reply.

The engine is persistence-ignorant and Discord-ignorant: it walks states + memory only.
"""
from __future__ import annotations

import logging
import random
import zlib
from dataclasses import dataclass

from .matchers import Intent, MatchResult
from .memory import Memory
from .normalize import normalize
from .responses import render
from .state import SetAction, State, Transition

logger = logging.getLogger(__name__)

_ON_ENTER_SALT = 0x9E3779B9  # distinct seed for on_enter vs reply in the same turn


def _transition_desc(t: "Transition", current: str) -> str:
    """Compact, log-friendly id of a transition: its key, matcher kind, and target."""
    if t.matcher is None:
        matcher = "always"
    elif isinstance(t.matcher, Intent):
        matcher = f"intent:{t.matcher.name}"
    else:
        matcher = type(t.matcher).__name__
    if t.goto is not None:
        target = t.goto
    elif t.push is not None:
        target = f"push:{t.push}"
    elif t.pop:
        target = "pop"
    else:
        target = current  # stays put (reply-only transition)
    return f"{t.key or '?'} matcher={matcher} -> {target}"


def _as_number(v, default=0):
    """Coerce a slot/capture value to int/float for counter math; default on failure."""
    try:
        n = float(v)
    except (TypeError, ValueError):
        return default
    return int(n) if n.is_integer() else n


@dataclass(frozen=True)
class StepResult:
    reply: str
    halted: bool


class EngineError(Exception):
    pass


class Engine:
    def __init__(self, states: dict[str, State], start_state: str, memory: Memory | None = None,
                 renderer=None):
        self.states = states
        self.start_state = start_state
        self.memory = memory or Memory()
        self.current = start_state
        # Last-fired transition + its match, for the affect layer to inspect (Phase 9).
        self._last_transition: Transition | None = None
        self._last_match: MatchResult | None = None
        # Session-only firing log for cooldown/once (transition key -> last-fired turn).
        self._fired: dict[str, int] = {}
        # Optional render hook: render(template, captures, state_name, salt) -> str.
        # The base engine uses plain §5.1 rendering; AffectEngine injects a grammar-aware
        # renderer so #symbol# references in reply:/on_enter: expand against live mode.
        self._renderer = renderer

    def _render(self, template: str, captures: dict, state_name: str, salt: int = 0) -> str:
        if self._renderer is not None:
            return self._renderer(template, captures, state_name, salt)
        return render(template, self.memory, captures, self.memory.turn, salt)

    # --- bootstrap ---

    def greeting(self) -> str:
        """Emit the start state's on_enter once, before any input."""
        s = self.states[self.start_state]
        if s.on_enter is None:
            return ""
        return self._render(s.on_enter, {}, self.start_state, _ON_ENTER_SALT)

    # --- the core loop ---

    def step(self, user_text: str) -> StepResult:
        state = self.states[self.current]
        self.memory.tick()
        norm = normalize(user_text)
        self.memory.record_input(norm.lower)

        # Stepping a terminal state again is a no-op halt (it has no transitions/fallback).
        # Callers normally stop at halt, but be defensive so a stray extra turn can't crash.
        if state.end:
            logger.debug("engine: state %s is terminal; no-op halt", self.current)
            return StepResult(reply="", halted=True)

        # Empty input never advances state — take fallback directly (DESIGN/Plan P3).
        if norm.cased == "":
            logger.debug("engine: empty input in %s -> fallback", self.current)
            return self._take(state, state.fallback, MatchResult(True), entered=False)

        chosen, result = self._select(state, norm)
        if chosen is None:
            logger.debug("engine: %s no transition matched %r -> fallback",
                         self.current, norm.lower)
            chosen, result = state.fallback, MatchResult(True)
        elif logger.isEnabledFor(logging.DEBUG):
            logger.debug("engine: %s matched %s captures=%s", self.current,
                         _transition_desc(chosen, self.current), result.captures or {})
        if chosen is None:
            # No transition matched and no fallback (validation prevents this for shipped
            # scripts; guard anyway rather than dereference None).
            logger.debug("engine: %s no fallback available -> halt", self.current)
            return StepResult(reply="", halted=state.end)

        return self._fire(state, chosen, result)

    def _select(self, state: State, norm) -> tuple[Transition | None, MatchResult]:
        for t in state.transitions:
            if not self._available(t):
                continue
            if t.matcher is None:
                mr = MatchResult(True)
            else:
                mr = t.matcher(norm)
                if not mr.matched:
                    continue
            if t.when is not None and not t.when.test(self.memory, mr.captures):
                continue
            return t, mr
        return None, MatchResult(False)

    def _available(self, t: Transition) -> bool:
        """False if a `once:` transition already fired, or a `cooldown:` hasn't elapsed."""
        if not t.key or not (t.once or t.cooldown):
            return True
        last = self._fired.get(t.key)
        if last is None:
            return True
        if t.once:
            return False
        return (self.memory.turn - last) >= t.cooldown

    def _fire(self, state: State, t: Transition, mr: MatchResult) -> StepResult:
        self._last_transition = t
        self._last_match = mr
        if t.key and (t.once or t.cooldown):
            self._fired[t.key] = self.memory.turn
        # act: write sets into memory (captures resolved against this turn's match)
        self._apply_sets(t, mr)
        self._apply_topic(t)

        # the reply belongs to the SOURCE state's grammar; capture it before moving
        source_name = self.current
        # move
        entered, target_name = self._move(state, t)
        return self._compose(t, mr, entered, target_name, source_name)

    def _apply_sets(self, t: Transition, mr: MatchResult) -> None:
        for sa in t.sets:
            self._apply_one_set(sa, mr)
        if t.sets and logger.isEnabledFor(logging.DEBUG):
            # Log which slots were touched (keys + op), not the values — keeps user
            # content out of the routine slot-write trail while still showing memory moves.
            logger.debug("engine: slots mutated: %s",
                         ", ".join(f"{sa.op}:{sa.key}" for sa in t.sets))

    def _apply_topic(self, t: Transition) -> None:
        """A transition's `topic:` sets the current topic and appends to the topic history."""
        if t.topic is None:
            return
        self.memory.set("topic", t.topic)
        seen = self.memory.get("topics_seen")
        lst = list(seen) if isinstance(seen, list) else []
        if t.topic not in lst:
            lst.append(t.topic)
        self.memory.set("topics_seen", lst)
        logger.debug("engine: topic -> %s", t.topic)

    def _resolve_expr(self, expr, mr: MatchResult):
        """Resolve one value expression: $capture, $$literal-dollar, or a plain literal."""
        if isinstance(expr, str) and expr.startswith("$$"):
            return expr[1:]
        if isinstance(expr, str) and expr.startswith("$"):
            name = expr[1:]
            if name not in mr.captures:
                raise EngineError(f"put: capture {expr} not produced by this turn's match")
            return mr.captures[name]
        return expr

    def _apply_one_set(self, sa: SetAction, mr: MatchResult) -> None:
        if sa.op == "clear":
            self.memory.clear(sa.key)
            return
        if sa.op == "rand":
            lo, hi = sa.value
            seed = self.memory.turn ^ zlib.crc32(sa.key.encode("utf-8"))
            self.memory.set(sa.key, random.Random(seed).randint(lo, hi), ttl=sa.ttl)
            return
        if sa.op == "put":
            kexpr, vexpr = sa.value
            k = self._resolve_expr(kexpr, mr)
            v = self._resolve_expr(vexpr, mr)
            d = dict(self.memory.get(sa.key) or {})
            d[str(k)] = v
            self.memory.set(sa.key, d, ttl=sa.ttl)
            return
        value = self._resolve_set_value(sa, mr)
        if sa.op == "inc":
            self.memory.set(sa.key, _as_number(self.memory.get(sa.key, 0)) + _as_number(value, 1),
                            ttl=sa.ttl)
        elif sa.op == "dec":
            self.memory.set(sa.key, _as_number(self.memory.get(sa.key, 0)) - _as_number(value, 1),
                            ttl=sa.ttl)
        elif sa.op == "append":
            cur = self.memory.get(sa.key)
            lst = list(cur) if isinstance(cur, list) else ([] if cur is None else [cur])
            lst.append(value)
            self.memory.set(sa.key, lst, ttl=sa.ttl)
        else:
            self.memory.set(sa.key, value, ttl=sa.ttl)

    def _resolve_set_value(self, sa: SetAction, mr: MatchResult):
        if not sa.is_capture_ref:
            return sa.value
        name = sa.value[1:]  # strip leading $
        if name not in mr.captures:
            raise EngineError(
                f"set {sa.key}={sa.value}: capture not produced by this turn's match"
            )
        return mr.captures[name]

    def _move(self, state: State, t: Transition) -> tuple[bool, str]:
        """Apply goto/push/pop. Returns (state_changed, target_name)."""
        if t.push is not None:
            self.memory.push_return(self.current)
            logger.debug("engine: push %s, goto %s", self.current, t.push)
            self.current = t.push
            return True, self.current
        if t.pop:
            ret = self.memory.pop_return()
            if ret is None:
                # empty stack: degrade to staying put (validation catches static cases)
                logger.debug("engine: pop with empty return stack in %s; staying", self.current)
                return False, self.current
            logger.debug("engine: pop %s -> %s", self.current, ret)
            self.current = ret
            return True, self.current
        if t.goto is not None and t.goto != self.current:
            self.current = t.goto
            return True, self.current
        # goto-to-self or no goto: stay
        return False, self.current

    def _compose(self, t: Transition, mr: MatchResult, entered: bool, target_name: str,
                 source_name: str) -> StepResult:
        pieces: list[str] = []
        reply_text = ""
        if t.reply is not None:
            reply_text = self._render(t.reply, mr.captures, source_name)
            if reply_text:
                pieces.append(reply_text)
        target = self.states[target_name]
        # The target's on_enter is a room intro. Only emit it when the transition itself
        # didn't reply — otherwise "<reply>. <room intro>" reads as two bolted-together
        # sentences. A silent state-change (push/pop/goto with no reply) still shows it.
        if entered and target.on_enter is not None and not reply_text:
            pieces.append(self._render(target.on_enter, {}, target_name, _ON_ENTER_SALT))
        text = " ".join(p for p in pieces if p)
        return StepResult(reply=text, halted=entered and target.end)

    def _take(self, state: State, t: Transition | None, mr: MatchResult, entered: bool) -> StepResult:
        """Fire a known transition (used for the empty-input fallback path)."""
        if t is None:
            return StepResult(reply="", halted=False)
        return self._fire(state, t, mr)
