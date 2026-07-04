"""Response grammar — the "speak module" (GRAMMAR.md, Plan Phase 8).

A recursive-descent expander over author-defined `grammar:` rules. `#symbol#` picks a
production (clock-seeded RNG) and recursively expands; productions are §5.1 templates,
so {slot}/$capture/{reflect:…}/#symbol# compose. Fetch functions (#recall:TAG#, #name#,
#count:slot#, #mode#) are a CLOSED vocabulary of pure reads into the episodic log/registers.

Mode-conditioned grammar: a state's baseline grammar, with per-mode overrides shadowing
individual symbols (default-and-override).

No model emits a word — every expansion was authored, so every output is traceable.
"""
from __future__ import annotations

import random
import re
import zlib
from dataclasses import dataclass
from typing import Protocol

from .memory import Memory
from .responses import _substitute  # single-pass §5.1 substitution

# #symbol# with an optional trailing text filter: #symbol|upper#, #recall:HOSTILE|title#.
_SYMBOL_RE = re.compile(r"#([a-zA-Z_][\w:]*)(\|[a-z]+)?#")
_DEPTH_CAP = 64  # runtime backstop; validation rejects true unbounded cycles

# Text filters applied to an expanded symbol's result.
_FILTERS = {"upper", "lower", "title", "shout", "reverse", "capitalize"}

# Weighted production: a trailing " *N" repeats the line N times in the choice pool.
# Requires whitespace before the '*' so incidental "5*2" style text is never mistaken.
_WEIGHT_RE = re.compile(r"^(.+?)\s+\*(\d+)$")


class GrammarError(Exception):
    pass


def _apply_filter(text: str, filterspec: str | None) -> str:
    """Apply a |filter (already including the leading '|') to an expanded string."""
    if not filterspec:
        return text
    f = filterspec[1:]
    if f == "upper":
        return text.upper()
    if f == "lower":
        return text.lower()
    if f == "title":
        return text.title()
    if f == "capitalize":
        return text[:1].upper() + text[1:] if text else text
    if f == "shout":
        return text.upper() + ("!" if not text.endswith("!") else "")
    if f == "reverse":
        return text[::-1]
    raise GrammarError(f"unknown grammar filter |{f}")


def _pick(rng: random.Random, productions):
    """Choose a production, honoring optional ' *N' weight suffixes (no suffix == weight 1).

    With no weighted entries the pool is identical to `productions`, so `rng.choice` picks
    the same element as before — the replay/determinism contract is preserved.
    """
    pool = []
    for p in productions:
        if isinstance(p, str):
            m = _WEIGHT_RE.match(p)
            if m:
                pool.extend([m.group(1)] * max(1, int(m.group(2))))
                continue
        pool.append(p)
    return rng.choice(pool)


class FetchContext(Protocol):
    """What the grammar may read. Implemented by the affect/episodic layer (Phase 9)."""

    def recall(self, tag: str) -> str | None: ...
    def name(self) -> str | None: ...
    def count(self, register: str) -> int: ...
    def mode(self) -> str: ...
    def turns(self) -> int: ...
    def get_topic(self) -> str: ...
    def get_daypart(self) -> str: ...
    def recap(self) -> str: ...
    def fact(self, key: str) -> str: ...


@dataclass
class NullFetch:
    """A FetchContext for grammar tests with no affect layer: everything empty."""

    _name: str | None = None
    _mode: str = "NEUTRAL"

    def recall(self, tag: str) -> str | None:
        return None

    def name(self) -> str | None:
        return self._name

    def count(self, register: str) -> int:
        return 0

    def mode(self) -> str:
        return self._mode

    def turns(self) -> int:
        return 0

    def get_topic(self) -> str:
        return "whatever we were on"

    def get_daypart(self) -> str:
        return "day"

    def recap(self) -> str:
        return "nothing worth recapping"

    def fact(self, key: str) -> str:
        return "a mystery"


def _resolve_grammar(baseline: dict, mode_grammar: dict, mode: str) -> dict:
    """Merge baseline with the active mode's per-symbol overrides (default-and-override)."""
    merged = dict(baseline)
    overrides = mode_grammar.get(mode, {})
    merged.update(overrides)
    return merged


def _fetch(name: str, ctx: FetchContext, rules: dict) -> str:
    """Resolve a fetch-function symbol. Empty results use a declared *_empty fallback rule."""
    if name == "name":
        v = ctx.name()
        if v is None:
            return _empty_fallback("name", rules)
        return v
    if name == "mode":
        return ctx.mode()
    if name == "turns":
        return str(ctx.turns())
    if name.startswith("rand:"):
        return _rand_fetch(name, ctx)
    if name.startswith("recall:"):
        tag = name[len("recall:") :]
        v = ctx.recall(tag)
        if v is None:
            return _empty_fallback(f"recall_{tag}", rules)
        return v
    if name.startswith("count:"):
        reg = name[len("count:") :]
        return str(ctx.count(reg))
    if name == "topic":
        return ctx.get_topic()
    if name == "daypart":
        return ctx.get_daypart()
    if name == "recap":
        return ctx.recap()
    if name.startswith("fact:"):
        return ctx.fact(name[len("fact:") :])
    raise GrammarError(f"unknown fetch function #{name}#")


# LO:HI (colon-separated so it fits the #symbol# charset [A-Za-z_][\w:]*, no regex change).
_RAND_RE = re.compile(r"(-?\d+):(-?\d+)")


def _rand_fetch(name: str, ctx: FetchContext) -> str:
    """#rand:LO:HI# -> a deterministic int in [LO, HI], seeded by the logical clock."""
    m = _RAND_RE.fullmatch(name[len("rand:"):])
    if not m:
        raise GrammarError(f"#{name}# must be of the form rand:LO:HI")
    lo, hi = int(m.group(1)), int(m.group(2))
    if lo > hi:
        lo, hi = hi, lo
    seed = ctx.turns() ^ zlib.crc32(name.encode("utf-8"))
    return str(random.Random(seed).randint(lo, hi))


def _empty_fallback(key: str, rules: dict) -> str:
    rule = f"{key}_empty"
    if rule not in rules:
        raise GrammarError(f"fetch #{key}# returned empty and no '{rule}' production declared")
    # The empty-fallback production is a plain terminal (no recursion expected).
    prod = rules[rule]
    return prod[0] if isinstance(prod, list) else str(prod)


def _is_fetch(symbol: str) -> bool:
    return (
        symbol in ("name", "mode", "turns", "topic", "daypart", "recap")
        or symbol.startswith("recall:")
        or symbol.startswith("count:")
        or symbol.startswith("rand:")
        or symbol.startswith("fact:")
    )


def expand(
    symbol: str,
    rules: dict,
    memory: Memory,
    captures: dict[str, str],
    ctx: FetchContext,
    clock: int,
    salt: int = 0,
) -> str:
    """Expand #symbol# recursively into a final string. Deterministic given (clock, salt)."""
    # zlib.crc32 is a STABLE hash (unlike hash(), which is per-process randomized) —
    # required so the same clock yields the same expansion across runs (replay contract).
    rng = random.Random(clock ^ salt ^ zlib.crc32(symbol.encode("utf-8")))
    return _expand(symbol, rules, memory, captures, ctx, rng, 0)


def _expand(symbol, rules, memory, captures, ctx, rng, depth) -> str:
    if depth > _DEPTH_CAP:
        raise GrammarError(f"grammar recursion exceeded depth cap at #{symbol}#")

    if _is_fetch(symbol):
        produced = _fetch(symbol, ctx, rules)
    else:
        if symbol not in rules:
            raise GrammarError(f"undefined grammar symbol #{symbol}#")
        productions = rules[symbol]
        if isinstance(productions, str):
            productions = [productions]
        produced = _pick(rng, productions)

    # Expand nested #symbols# first (recursively), then resolve §5.1 tokens.
    def repl(m):
        inner = _expand(m.group(1), rules, memory, captures, ctx, rng, depth + 1)
        return _apply_filter(inner, m.group(2))

    expanded = _SYMBOL_RE.sub(repl, produced)
    return _substitute(expanded, memory, captures)


def expand_entry(
    baseline: dict,
    mode_grammar: dict,
    mode: str,
    entry: str,
    memory: Memory,
    captures: dict[str, str],
    ctx: FetchContext,
    clock: int,
    salt: int = 0,
) -> str:
    """Resolve mode overrides, then expand the entry symbol (e.g. 'reply')."""
    rules = _resolve_grammar(baseline, mode_grammar, mode)
    return expand(entry, rules, memory, captures, ctx, clock, salt)


def expand_template(
    template: str,
    baseline: dict,
    mode_grammar: dict,
    mode: str,
    memory: Memory,
    captures: dict[str, str],
    ctx: FetchContext,
    clock: int,
    salt: int = 0,
) -> str:
    """Expand a raw reply/on_enter template that may contain #symbol# references.

    This is what lets the recursive grammar drive LIVE output (reply:/on_enter:), not
    just the separate render_grammar_reply() path. Steps:
      1. pick a top-level |-alternative (turn-seeded, same rule as responses.render),
      2. expand every #symbol# against the mode-resolved grammar (recursively),
      3. resolve §5.1 tokens ({slot}/$cap/{reflect:}).
    A template with no #symbol# and no grammar behaves exactly like responses.render.
    """
    from .responses import split_alternatives

    rules = _resolve_grammar(baseline or {}, mode_grammar or {}, mode)

    alts = split_alternatives(template)
    if len(alts) == 1:
        chosen = alts[0]
    else:
        chosen = random.Random(clock ^ salt).choice(alts)

    # Expand #symbol# references inside the chosen alternative.
    rng = random.Random(clock ^ salt ^ zlib.crc32(b"template"))

    def repl(m):
        inner = _expand(m.group(1), rules, memory, captures, ctx, rng, 0)
        return _apply_filter(inner, m.group(2))

    expanded = _SYMBOL_RE.sub(repl, chosen)
    return _substitute(expanded, memory, captures)


# ----------------------------------------------------------------------------
# Load-time validation (called by script.py, Plan Phase 4/8).
# ----------------------------------------------------------------------------

_KNOWN_FETCH_PREFIXES = ("recall:", "count:", "rand:", "fact:")
_KNOWN_FETCH_EXACT = ("name", "mode", "turns", "topic", "daypart", "recap")


def _symbols_in(production: str) -> set[str]:
    # group(1) is the symbol name; group(2) is the optional |filter (ignored for resolution).
    return {m.group(1) for m in _SYMBOL_RE.finditer(production)}


def validate_grammar(rules: dict, where: str, known_modes: set[str] | None = None) -> None:
    """Every #symbol# resolves; fetches are in-vocab with empty-fallbacks; no unbounded cycle."""
    from .script import ScriptError  # local import to avoid a cycle

    def productions_of(sym: str) -> list[str]:
        p = rules.get(sym)
        if isinstance(p, str):
            return [p]
        return list(p or [])

    # 0. grammar filters must be in-vocab (#sym|upper#).
    for sym, prods in rules.items():
        for prod in (prods if isinstance(prods, list) else [prods]):
            for m in _SYMBOL_RE.finditer(prod):
                if m.group(2) and m.group(2)[1:] not in _FILTERS:
                    raise ScriptError(f"{where}: unknown grammar filter {m.group(2)} on #{m.group(1)}#")

    # 1. resolution + fetch vocab + empty-fallback presence
    for sym, prods in rules.items():
        for prod in (prods if isinstance(prods, list) else [prods]):
            for ref in _symbols_in(prod):
                if _is_fetch(ref):
                    if not (ref in _KNOWN_FETCH_EXACT or ref.startswith(_KNOWN_FETCH_PREFIXES)):
                        raise ScriptError(f"{where}: unknown fetch function #{ref}#")
                    # empty-fallback required for name / recall:*
                    if ref == "name" and "name_empty" not in rules:
                        raise ScriptError(f"{where}: #name# needs a 'name_empty' production")
                    if ref.startswith("recall:"):
                        tag = ref[len("recall:") :]
                        if f"recall_{tag}_empty" not in rules:
                            raise ScriptError(f"{where}: #recall:{tag}# needs a 'recall_{tag}_empty' production")
                    if ref.startswith("rand:") and not _RAND_RE.fullmatch(ref[len("rand:"):]):
                        raise ScriptError(f"{where}: #{ref}# must be of the form rand:LO:HI")
                elif ref not in rules:
                    raise ScriptError(f"{where}: undefined grammar symbol #{ref}#")

    # 2. unbounded-cycle detection: a symbol whose every production path must re-enter
    #    a symbol on the current stack with no terminal escape.
    def terminates(sym: str, stack: frozenset[str]) -> bool:
        if _is_fetch(sym):
            return True
        if sym in stack:
            return False  # cycle back to an in-progress symbol on THIS path
        new_stack = stack | {sym}
        # symbol terminates if AT LEAST ONE production can fully terminate
        for prod in productions_of(sym):
            refs = _symbols_in(prod)
            if all(terminates(r, new_stack) for r in refs):
                return True
        return False

    for sym in rules:
        if sym.endswith("_empty"):
            continue
        if not terminates(sym, frozenset()):
            raise ScriptError(f"{where}: grammar symbol #{sym}# has no terminating expansion (unbounded recursion)")
