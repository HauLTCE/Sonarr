"""Static analysis & inspection helpers for a loaded script (Plan: tooling QoL batch).

Pure, read-only functions over a LoadedScript — used by the `stats`, `lint`, `explain`,
`sample`, and `pools` CLI subcommands. No engine state, no wall-clock, deterministic.
"""
from __future__ import annotations

import random
import re
import zlib
from dataclasses import dataclass, field

from .grammar import _SYMBOL_RE, _is_fetch, _resolve_grammar, _expand, _symbols_in
from .matchers import Always, Caps, FuzzyKeyword, Intent, Keyword, MatchResult, Regex
from .memory import Memory
from .normalize import normalize
from .script import LoadedScript
from .state import State, Transition

_REF_RE = _SYMBOL_RE  # #symbol# references


# ----------------------------------------------------------------------------
# Reference collection
# ----------------------------------------------------------------------------

def _templates_of(state: State) -> list[str]:
    """Every author-written template string attached to a state (on_enter + replies)."""
    out: list[str] = []
    if state.on_enter:
        out.append(state.on_enter)
    edges = list(state.transitions) + ([state.fallback] if state.fallback else [])
    for t in edges:
        if t.reply:
            out.append(t.reply)
    return out


def _productions_of(grammar: dict) -> list[str]:
    out: list[str] = []
    for prods in grammar.values():
        if isinstance(prods, str):
            out.append(prods)
        else:
            out.extend(str(p) for p in (prods or []))
    return out


def _refs_in(strings: list[str]) -> set[str]:
    refs: set[str] = set()
    for s in strings:
        # _REF_RE has two groups (symbol, |filter); take the symbol name only.
        refs.update(m.group(1) for m in _REF_RE.finditer(s))
    return refs


def all_symbol_refs(loaded: LoadedScript) -> set[str]:
    """Every #symbol# referenced anywhere: templates + local grammar + mode grammar + pools."""
    strings: list[str] = []
    for state in loaded.states.values():
        strings.extend(_templates_of(state))
        strings.extend(_productions_of(state.grammar or {}))
        for overrides in (state.mode_grammar or {}).values():
            strings.extend(_productions_of(overrides))
    strings.extend(_productions_of(loaded.global_grammar or {}))
    return _refs_in(strings)


# ----------------------------------------------------------------------------
# Stats
# ----------------------------------------------------------------------------

def script_stats(loaded: LoadedScript) -> dict:
    states = loaded.states
    n_trans = sum(len(s.transitions) for s in states.values())
    n_fallbacks = sum(1 for s in states.values() if s.fallback is not None)
    n_local_sym = sum(len(s.grammar or {}) for s in states.values())
    pools = loaded.global_grammar or {}
    pool_lines = sum(len(v) if isinstance(v, list) else 1 for v in pools.values())
    return {
        "states": len(states),
        "terminal_states": sum(1 for s in states.values() if s.end),
        "affect_states": list(loaded.affect_states or []),
        "start": loaded.start,
        "transitions": n_trans,
        "fallbacks": n_fallbacks,
        "intents": len(loaded.intents),
        "modes": list((loaded.modes or {}).keys()),
        "local_grammar_symbols": n_local_sym,
        "pools": len(pools),
        "pool_lines": pool_lines,
        "transitions_per_state": {n: len(s.transitions) for n, s in states.items()},
    }


# ----------------------------------------------------------------------------
# Lint checks (each returns a list of human-readable warnings)
# ----------------------------------------------------------------------------

def used_intent_names(loaded: LoadedScript) -> set[str]:
    used: set[str] = set()
    for state in loaded.states.values():
        for t in list(state.transitions) + ([state.fallback] if state.fallback else []):
            if isinstance(t.matcher, Intent):
                used.add(t.matcher.name)
    return used


def unused_intents(loaded: LoadedScript) -> list[str]:
    return sorted(set(loaded.intents) - used_intent_names(loaded))


def unused_pools(loaded: LoadedScript) -> list[str]:
    """Global pool symbols never referenced by any template/production."""
    refs = all_symbol_refs(loaded)
    return sorted(sym for sym in (loaded.global_grammar or {}) if sym not in refs)


def unused_grammar_symbols(loaded: LoadedScript) -> dict[str, list[str]]:
    """Per state: local grammar symbols defined but never referenced within that state.

    Excludes `*_empty` productions (referenced implicitly by fetch fallbacks).
    """
    out: dict[str, list[str]] = {}
    for name, state in loaded.states.items():
        grammar = state.grammar or {}
        if not grammar:
            continue
        strings = _templates_of(state) + _productions_of(grammar)
        for overrides in (state.mode_grammar or {}).values():
            strings += _productions_of(overrides)
        refs = _refs_in(strings)
        dead = [s for s in grammar if s not in refs and not s.endswith("_empty")]
        if dead:
            out[name] = sorted(dead)
    return out


def grammar_coverage(loaded: LoadedScript) -> dict:
    """Reachability report over the shared pools: which are referenced (transitively from
    any template/local grammar) and how many authored lines are reachable vs orphaned."""
    pools = loaded.global_grammar or {}
    referenced: set[str] = set()
    stack = [s for s in all_symbol_refs(loaded) if not _is_fetch(s)]
    while stack:
        s = stack.pop()
        if s in referenced:
            continue
        referenced.add(s)
        prods = pools.get(s)
        if prods is not None:
            for p in (prods if isinstance(prods, list) else [prods]):
                for r in _symbols_in(str(p)):
                    if not _is_fetch(r) and r not in referenced:
                        stack.append(r)
    pool_syms = set(pools)
    ref_pools = referenced & pool_syms

    def n_lines(v):
        return len(v) if isinstance(v, list) else 1

    reachable = sum(n_lines(pools[s]) for s in ref_pools)
    total = sum(n_lines(v) for v in pools.values())
    return {
        "pools": len(pools),
        "referenced_pools": len(ref_pools),
        "unreferenced_pools": len(pool_syms - ref_pools),
        "reachable_lines": reachable,
        "total_lines": total,
        "orphan_lines": total - reachable,
        "unreferenced": sorted(pool_syms - ref_pools),
    }


def unreachable_transitions(loaded: LoadedScript) -> list[str]:
    """Transitions shadowed by an earlier unconditional (Always + no `when`) transition."""
    warnings: list[str] = []
    for name, state in loaded.states.items():
        blocked_by = None
        for i, t in enumerate(state.transitions):
            if blocked_by is not None:
                warnings.append(
                    f"state {name}: transition[{i}] is unreachable "
                    f"(transition[{blocked_by}] is unconditional)"
                )
                continue
            if isinstance(t.matcher, Always) and t.when is None:
                blocked_by = i
    return warnings


def lint(loaded: LoadedScript) -> list[str]:
    """Aggregate non-fatal quality warnings (the script already passed load validation)."""
    warnings: list[str] = []
    for name in unused_intents(loaded):
        warnings.append(f"intent {name!r} is defined but never used by any transition")
    for state, syms in unused_grammar_symbols(loaded).items():
        for s in syms:
            warnings.append(f"state {state}: grammar symbol #{s}# is defined but never referenced")
    warnings.extend(unreachable_transitions(loaded))
    dead_pools = unused_pools(loaded)
    if dead_pools:
        warnings.append(f"{len(dead_pools)} pool(s) are never referenced "
                        f"(informational; the shared bank is intentionally large)")
    return warnings


# ----------------------------------------------------------------------------
# Route explanation (why did input X go to Y from state S?)
# ----------------------------------------------------------------------------

@dataclass
class RouteMatch:
    index: int          # transition index, or -1 for fallback
    matcher: str
    when_ok: bool
    matched: bool
    fired: bool         # the first fully-matching transition
    target: str


def _matcher_label(t: Transition) -> str:
    m = t.matcher
    if m is None:
        return "when-only"
    if isinstance(m, Always):
        return "always"
    if isinstance(m, Keyword):
        return "keyword"
    if isinstance(m, FuzzyKeyword):
        return "fuzzy_keyword"
    if isinstance(m, Regex):
        return f"regex:{m.source[:40]}"
    if isinstance(m, Intent):
        return f"intent:{m.name}"
    if isinstance(m, Caps):
        return "caps"
    return type(m).__name__


def _target_label(name: str, t: Transition) -> str:
    if t.push is not None:
        return f"push {t.push}"
    if t.pop:
        return "pop"
    if t.goto is not None:
        return t.goto
    return name  # stays


def explain_route(loaded: LoadedScript, state_name: str, text: str,
                  slots: dict | None = None) -> list[RouteMatch]:
    """Evaluate a state's transitions against `text`, reporting each candidate.

    Guards (`when:`) are evaluated against a Memory built from `slots` (default empty),
    exactly as the engine would (matcher AND when). The first fully-matching transition
    is marked fired=True (declared-order precedence).
    """
    if state_name not in loaded.states:
        raise KeyError(f"no such state {state_name!r}")
    state = loaded.states[state_name]
    mem = Memory()
    for k, v in (slots or {}).items():
        mem.set(k, v)
    norm = normalize(text)

    results: list[RouteMatch] = []
    fired = False
    edges = list(state.transitions)
    for i, t in enumerate(edges):
        mr = MatchResult(True) if t.matcher is None else t.matcher(norm)
        matched = mr.matched
        when_ok = t.when is None or t.when.test(mem, mr.captures)
        full = matched and when_ok
        this_fired = full and not fired
        if this_fired:
            fired = True
        results.append(RouteMatch(i, _matcher_label(t), when_ok, matched,
                                   this_fired, _target_label(state_name, t)))
    if not fired and state.fallback is not None:
        results.append(RouteMatch(-1, "fallback", True, True, True,
                                  _target_label(state_name, state.fallback)))
    return results


# ----------------------------------------------------------------------------
# Grammar sampling (preview N expansions of a symbol)
# ----------------------------------------------------------------------------

def intent_summary(loaded: LoadedScript) -> list[tuple[str, str]]:
    """(intent name, human trigger summary) for every declared intent."""
    out: list[tuple[str, str]] = []
    for name in sorted(loaded.intents):
        parts: list[str] = []
        for m in loaded.intents[name].matcher.members:
            if isinstance(m, Keyword):
                parts.append("kw[" + ",".join(sorted(m.words)) + "]")
            elif isinstance(m, FuzzyKeyword):
                parts.append("fuzzy[" + ",".join(sorted(m.words)) + "]")
            elif isinstance(m, Regex):
                parts.append("re:" + m.source)
        used = name in used_intent_names(loaded)
        summary = "; ".join(parts)
        out.append((name + ("" if used else " (unused)"), summary))
    return out


def diff_scripts(a: LoadedScript, b: LoadedScript) -> dict:
    """Structural diff: what states/intents/modes/pools were added/removed a -> b."""
    def d(sa, sb) -> dict:
        return {"added": sorted(set(sb) - set(sa)), "removed": sorted(set(sa) - set(sb))}
    return {
        "states": d(a.states, b.states),
        "intents": d(a.intents, b.intents),
        "modes": d(a.modes or {}, b.modes or {}),
        "pools": d(a.global_grammar or {}, b.global_grammar or {}),
    }


_FUZZ_WORDS = [
    "hi", "hello", "you", "idiot", "trash", "love you", "the", "game", "why", "how",
    "flip a coin", "roll a dice", "rock", "paper", "scissors", "sad", "cool", "bye",
    "123", "\U0001F600", "STOP YELLING", "sooo good", "@bot", "my name is Sam",
    "what can you do", "i'm 25", "my favorite color is blue", "vibe check", "",
]


def fuzz_inputs(n: int, seed: int = 0) -> list[str]:
    """Deterministic pseudo-random inputs for crash-testing (seeded)."""
    rng = random.Random(seed)
    out = []
    for _ in range(n):
        k = rng.randint(0, 6)
        out.append(" ".join(rng.choice(_FUZZ_WORDS) for _ in range(k)).strip())
    return out


@dataclass
class _PreviewFetch:
    """A FetchContext for previews: a fixed name, NEUTRAL mode, empty log, turn 0."""

    _name: str = "you"

    def recall(self, tag: str) -> str | None:
        return None

    def name(self) -> str | None:
        return self._name

    def count(self, register: str) -> int:
        return 0

    def mode(self) -> str:
        return "NEUTRAL"

    def turns(self) -> int:
        return 0


def sample_symbol(loaded: LoadedScript, symbol: str, n: int = 10,
                  state_name: str | None = None) -> list[str]:
    """Expand #symbol# n times (clocks 0..n-1) against the pools (+ optional state grammar).

    Deterministic and side-effect free. Render errors (e.g. a fetch with no fallback in
    this scope) are captured inline as <error: ...> rather than raised, so previews are safe.
    """
    rules = dict(loaded.global_grammar or {})
    if state_name and state_name in loaded.states:
        rules.update(loaded.states[state_name].grammar or {})
    mem = Memory()
    mem.set("name", "you")
    ctx = _PreviewFetch()
    out: list[str] = []
    for clock in range(n):
        rng = random.Random(clock ^ zlib.crc32(symbol.encode("utf-8")))
        try:
            out.append(_expand(symbol, rules, mem, {}, ctx, rng, 0))
        except Exception as e:  # noqa: BLE001 — preview must never crash
            out.append(f"<error: {type(e).__name__}: {e}>")
    return out
