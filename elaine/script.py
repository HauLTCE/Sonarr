"""YAML script loader + static validation (DESIGN §6, Plan Phase 4).

load(path) parses the schema into (states, start_state, intents) and runs fail-fast
static validation BEFORE any turn, each error naming the offending state/transition.

The matcher/guard parsers here are the single source of truth for the YAML schema.
Phase 5 adds push/pop; Phase 8 adds grammar; Phase 9 adds personality/modes.
"""
from __future__ import annotations

import os
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

from . import guards as G
from . import matchers as M
from .responses import split_alternatives
from .state import SetAction, State, Transition


class _ElaineLoader(yaml.SafeLoader):
    """SafeLoader without YAML 1.1's yes/no/on/off bool coercion.

    PyYAML follows YAML 1.1, where `yes`, `no`, `on`, `off` resolve to booleans —
    so `keyword: [yes, no]` would silently become `[True, False]` and never match the
    words "yes"/"no". We keep only true/false/True/False as booleans; everything else
    stays a plain string.
    """


# Rebuild the implicit bool resolver to accept only true/false (drop yes/no/on/off).
_ElaineLoader.yaml_implicit_resolvers = {
    k: [(tag, regexp) for tag, regexp in v if tag != "tag:yaml.org,2002:bool"]
    for k, v in yaml.SafeLoader.yaml_implicit_resolvers.items()
}
_ElaineLoader.add_implicit_resolver(
    "tag:yaml.org,2002:bool",
    re.compile(r"^(?:true|True|TRUE|false|False|FALSE)$"),
    list("tTfF"),
)


class ScriptError(Exception):
    """Raised on any static validation failure, with a message naming the location."""


# ----------------------------------------------------------------------------
# Template static check (§5.1 grammar): balanced braces, known forms, valid escapes.
# Returns the set of capture refs ($name) and slot refs ({slot}) used.
# ----------------------------------------------------------------------------

_CAP_RE = re.compile(r"[A-Za-z_]\w*|0")


@dataclass
class TemplateRefs:
    captures: set[str] = field(default_factory=set)
    slots: set[str] = field(default_factory=set)
    symbols: set[str] = field(default_factory=set)


def check_template(template: str, where: str) -> TemplateRefs:
    refs = TemplateRefs()
    for alt in split_alternatives(template):
        _check_alt(alt, where, refs)
    return refs


def _check_alt(t: str, where: str, refs: TemplateRefs) -> None:
    i, n = 0, len(t)
    while i < n:
        c = t[i]
        if c == "{" and i + 1 < n and t[i + 1] == "{":
            i += 2; continue
        if c == "}" and i + 1 < n and t[i + 1] == "}":
            i += 2; continue
        if c == "$" and i + 1 < n and t[i + 1] == "$":
            i += 2; continue
        if c == "\\" and i + 1 < n and t[i + 1] == "|":
            i += 2; continue
        if c == "}":
            raise ScriptError(f"{where}: unbalanced '}}' in template {t!r}")
        if c == "{":
            close = t.find("}", i + 1)
            if close == -1:
                raise ScriptError(f"{where}: unbalanced '{{' in template {t!r}")
            body = t[i + 1 : close]
            if body.startswith("reflect:"):
                arg = body[len("reflect:") :]
                if not arg.startswith("$") or not _CAP_RE.fullmatch(arg[1:]):
                    raise ScriptError(f"{where}: reflect: needs a $capture, got {body!r}")
                refs.captures.add(arg[1:])
            else:
                if not re.fullmatch(r"[A-Za-z_]\w*", body):
                    raise ScriptError(f"{where}: bad slot name {{{body}}}")
                refs.slots.add(body)
            i = close + 1
            continue
        if c == "$":
            m = _CAP_RE.match(t, i + 1)
            if not m:
                raise ScriptError(f"{where}: malformed capture ref in {t!r}")
            refs.captures.add(m.group(0))
            i = m.end()
            continue
        if c == "#":
            close = t.find("#", i + 1)
            if close == -1:
                raise ScriptError(f"{where}: unbalanced '#' (grammar symbol) in {t!r}")
            sym = t[i + 1 : close]
            if not sym:
                raise ScriptError(f"{where}: empty grammar symbol '##' in {t!r}")
            refs.symbols.add(sym.split("|", 1)[0])  # strip optional |filter for resolution
            i = close + 1
            continue
        i += 1


# ----------------------------------------------------------------------------
# Matcher parsing. Returns (matcher, produced_capture_names).
# produced_capture_names = the named groups this matcher can yield (for $ref checks).
# ----------------------------------------------------------------------------

def _regex_group_names(pattern: str, where: str) -> set[str]:
    try:
        compiled = re.compile(pattern)
    except re.error as e:
        raise ScriptError(f"{where}: bad regex {pattern!r}: {e}")
    names = set(compiled.groupindex.keys())
    names.add("0")  # whole match always available
    return names


def parse_matcher(spec: dict, where: str, intents: dict[str, "ParsedIntent"]) -> tuple[M.Matcher, set[str]]:
    keys = set(spec.keys())
    known = {"keyword", "fuzzy_keyword", "regex", "intent", "always", "max_distance", "min_len",
             "ml_intent", "threshold", "model", "caps", "min_letters", "min_ratio",
             "number", "starts_with", "ends_with", "all_keywords", "length",
             "emoji", "elongated", "url", "mention"}
    unknown = keys - known
    if unknown:
        raise ScriptError(f"{where}: unknown matcher key(s) {sorted(unknown)}")

    if "always" in spec:
        return M.always(), set()
    if "caps" in spec:
        return M.caps(min_letters=int(spec.get("min_letters", 3)),
                      min_ratio=float(spec.get("min_ratio", 0.7))), set()
    if "number" in spec:
        return M.number(), {"0", "number"}
    if "starts_with" in spec:
        return M.starts_with(spec["starts_with"]), set()
    if "ends_with" in spec:
        return M.ends_with(spec["ends_with"]), set()
    if "all_keywords" in spec:
        return M.all_keywords(spec["all_keywords"]), set()
    if "length" in spec:
        bounds = spec["length"] or {}
        allowed = {"min_words", "max_words", "min_chars", "max_chars"}
        bad = set(bounds) - allowed
        if bad:
            raise ScriptError(f"{where}: length unknown bound(s) {sorted(bad)}")
        return M.length(**{k: int(v) for k, v in bounds.items()}), set()
    if "emoji" in spec:
        return M.emoji(), set()
    if "elongated" in spec:
        return M.elongated(), set()
    if "url" in spec:
        return M.url(), set()
    if "mention" in spec:
        return M.mention(), set()
    if "keyword" in spec:
        return M.keyword(spec["keyword"]), set()
    if "fuzzy_keyword" in spec:
        md = int(spec.get("max_distance", 1))
        ml = int(spec.get("min_len", 4))
        return M.fuzzy_keyword(spec["fuzzy_keyword"], max_distance=md, min_len=ml), set()
    if "regex" in spec:
        pat = spec["regex"]
        names = _regex_group_names(pat, where)
        return M.regex(pat), names
    if "intent" in spec:
        name = spec["intent"]
        if name not in intents:
            raise ScriptError(f"{where}: unresolved intent {name!r}")
        return intents[name].matcher, intents[name].produced
    if "ml_intent" in spec:
        return _parse_ml_intent(spec, where), set()
    raise ScriptError(f"{where}: matcher has no recognized key (got {sorted(keys)})")


def _parse_ml_intent(spec: dict, where: str) -> M.Matcher:
    """Build an MLIntent matcher; degrade to a never-match stub if the model is absent.

    threshold must be in [0,1]. A referenced model that's present is loaded and its label
    set checked; absence is graceful (the transition simply never fires, fallback covers).
    """
    from .classifier import MLIntent, ModelUnavailable, load_model

    name = spec["ml_intent"]
    threshold = float(spec.get("threshold", 0.5))
    if not (0.0 <= threshold <= 1.0):
        raise ScriptError(f"{where}: ml_intent threshold {threshold} not in [0,1]")
    model_path = spec.get("model", os.environ.get("ELAINE_MODEL", "model.pkl"))
    try:
        model = load_model(model_path)
    except ModelUnavailable:
        # Graceful absence: a matcher that never fires (symbolic fallback covers the turn).
        return _NeverMatch()
    if name not in model.labels:
        raise ScriptError(f"{where}: ml_intent label {name!r} not in model label set {model.labels}")
    return MLIntent(name=name, threshold=threshold, model=model)


class _NeverMatch:
    """Stand-in for an ml_intent whose model is unavailable — always misses, no captures."""

    def __call__(self, norm):
        return M.FALSE


@dataclass
class ParsedIntent:
    matcher: M.Intent
    produced: set[str]  # captures reachable via any regex member


def parse_intents(raw: dict | None) -> dict[str, ParsedIntent]:
    out: dict[str, ParsedIntent] = {}
    for name, spec in (raw or {}).items():
        members: list[M.Matcher] = []
        produced: set[str] = set()
        # keyword member
        if "keyword" in spec:
            members.append(M.keyword(spec["keyword"]))
        if "fuzzy_keyword" in spec:
            md = int(spec.get("max_distance", 1))
            ml = int(spec.get("min_len", 4))
            members.append(M.fuzzy_keyword(spec["fuzzy_keyword"], max_distance=md, min_len=ml))
        if "regex" in spec:
            pats = spec["regex"]
            if isinstance(pats, str):
                pats = [pats]
            for pat in pats:
                produced |= _regex_group_names(pat, f"intent {name}")
                members.append(M.regex(pat))
        if not members:
            raise ScriptError(f"intent {name!r}: must have keyword/fuzzy_keyword/regex member(s)")
        out[name] = ParsedIntent(M.Intent(name, tuple(members)), produced)
    return out


# ----------------------------------------------------------------------------
# Guard (`when:`) parsing.
# ----------------------------------------------------------------------------

def parse_guard(spec: Any, where: str) -> G.Guard:
    if not isinstance(spec, dict) or len(spec) != 1:
        raise ScriptError(f"{where}: `when` must be a single-key mapping, got {spec!r}")
    (op, arg), = spec.items()
    if op == "has":
        return G.Has(str(arg))
    if op == "equals":
        if not (isinstance(arg, list) and len(arg) == 2):
            raise ScriptError(f"{where}: equals expects [slot, literal], got {arg!r}")
        return G.Equals(str(arg[0]), arg[1])
    if op == "not":
        return G.Not(parse_guard(arg, where))
    if op == "all":
        return G.All(tuple(parse_guard(p, where) for p in arg))
    if op == "any":
        return G.Any_(tuple(parse_guard(p, where) for p in arg))
    if op == "turn_lt":
        return G.TurnLt(int(arg))
    if op == "turn_ge":
        return G.TurnGe(int(arg))
    if op in ("slot_gt", "slot_ge", "slot_lt", "slot_le"):
        if not (isinstance(arg, list) and len(arg) == 2):
            raise ScriptError(f"{where}: {op} expects [operand, operand], got {arg!r}")
        try:
            return G.SlotCmp(G._operand(arg[0]), op[len("slot_"):], G._operand(arg[1]))
        except ValueError as e:
            raise ScriptError(f"{where}: {op}: {e}")
    if op == "matches":
        if not (isinstance(arg, list) and len(arg) == 2):
            raise ScriptError(f"{where}: matches expects [slot, regex], got {arg!r}")
        try:
            pat = re.compile(str(arg[1]))
        except re.error as e:
            raise ScriptError(f"{where}: matches bad regex {arg[1]!r}: {e}")
        return G.Matches(str(arg[0]), pat)
    if op == "contains":
        if not (isinstance(arg, list) and len(arg) == 2):
            raise ScriptError(f"{where}: contains expects [slot, substring], got {arg!r}")
        return G.Contains(str(arg[0]), str(arg[1]))
    if op == "slot_in":
        if not (isinstance(arg, list) and len(arg) == 2 and isinstance(arg[1], list)):
            raise ScriptError(f"{where}: slot_in expects [slot, [options]], got {arg!r}")
        return G.SlotIn(str(arg[0]), tuple(arg[1]))
    raise ScriptError(f"{where}: unknown `when` op {op!r}")


# ----------------------------------------------------------------------------
# Transition parsing.
# ----------------------------------------------------------------------------

_SET_OPS = ("inc", "dec", "append", "clear", "rand", "put")

# Closed vocabulary for a transition's `act:` (Phase 4). Kept small and declarative: each
# names what a reply DOES in the conversation, so the next turn can react coherently
# (the affect layer records the fired act as the `bot_last_act` slot). Extend deliberately.
_SPEECH_ACTS = frozenset({
    "greeting",     # opens/acknowledges contact
    "question",     # asks something, expects an answer
    "answer",       # answers the user's question
    "statement",    # a plain assertion
    "dismissal",    # brushes the user off
    "deflect",      # dodges (flirting, prying)
    "insult",       # attacks
    "comeback",     # retaliates to an attack
    "farewell",     # ends the conversation
    "acknowledge",  # minimal ack ("noted.")
})


def _parse_sets(raw: dict | None, where: str) -> tuple[SetAction, ...]:
    out: list[SetAction] = []
    for key, val in (raw or {}).items():
        ttl = None
        op = "set"
        value = val
        # Mapping forms: {value:, ttl:} (TTL), or one of the mutation ops (inc/dec/append/...).
        if isinstance(val, dict):
            present_ops = [o for o in _SET_OPS if o in val]
            if len(present_ops) > 1:
                raise ScriptError(f"{where}: set {key!r} has conflicting ops {present_ops}")
            if present_ops:
                op = present_ops[0]
                value = val[op]
                ttl = int(val["ttl"]) if "ttl" in val else None
                if op == "clear":
                    value = None
                elif op == "rand":
                    if not (isinstance(value, list) and len(value) == 2):
                        raise ScriptError(f"{where}: set {key!r} rand expects [lo, hi], got {value!r}")
                    value = [int(value[0]), int(value[1])]
                elif op == "put":
                    if not (isinstance(value, list) and len(value) == 2):
                        raise ScriptError(f"{where}: set {key!r} put expects [key, value], got {value!r}")
            elif "value" in val or "ttl" in val:
                ttl = int(val["ttl"]) if "ttl" in val else None
                value = val.get("value")
            else:
                raise ScriptError(f"{where}: set {key!r} has an unknown mapping form {val!r}")
        is_ref = (op in ("set", "inc", "dec", "append")
                  and isinstance(value, str) and value.startswith("$") and not value.startswith("$$"))
        if isinstance(value, str) and value.startswith("$$"):
            value = value[1:]  # $$ -> literal leading $
        out.append(SetAction(key=key, value=value, is_capture_ref=is_ref, ttl=ttl, op=op))
    return out


def parse_transition(raw: dict, where: str, intents: dict[str, ParsedIntent],
                     is_fallback: bool = False, key: str = "") -> Transition:
    keys = set(raw.keys())
    known = {"match", "when", "set", "goto", "push", "pop", "reply", "affect", "remember",
             "topic", "cooldown", "once", "act"}
    unknown = keys - known
    if unknown:
        raise ScriptError(f"{where}: unknown transition key(s) {sorted(unknown)}")

    matcher = None
    produced: set[str] = set()
    if "match" in raw:
        matcher, produced = parse_matcher(raw["match"], where, intents)
    elif is_fallback:
        matcher = M.always()  # a fallback is an implicit always() (DESIGN §4)

    when = parse_guard(raw["when"], where) if "when" in raw else None
    if matcher is None and when is None:
        raise ScriptError(f"{where}: transition needs a `match` or a `when`")

    sets = _parse_sets(raw.get("set"), where)

    # action verb mutual exclusivity
    verbs = [v for v in ("goto", "push") if v in raw]
    if raw.get("pop"):
        verbs.append("pop")
    if len(verbs) > 1:
        raise ScriptError(f"{where}: at most one of goto/push/pop, got {verbs}")

    goto = raw.get("goto")
    push = raw.get("push")
    pop = bool(raw.get("pop"))
    reply = raw.get("reply")

    # affect: register deltas, e.g. { anger: +2, warmth: -1 }
    affect_raw = raw.get("affect") or {}
    affect = tuple((str(k), float(v)) for k, v in affect_raw.items())

    # remember: a capture ref to log as a quote (must be producible by the matcher)
    remember = raw.get("remember")
    if remember is not None:
        if not (isinstance(remember, str) and remember.startswith("$")):
            raise ScriptError(f"{where}: remember must be a capture ref like $0 or $jab")

    topic = str(raw["topic"]) if raw.get("topic") is not None else None
    cooldown = int(raw.get("cooldown", 0))
    once = bool(raw.get("once", False))

    # act: declarative speech-act label (Phase 4), validated against the closed vocabulary.
    act = raw.get("act")
    if act is not None:
        act = str(act)
        if act not in _SPEECH_ACTS:
            raise ScriptError(
                f"{where}: unknown act {act!r}; allowed: {sorted(_SPEECH_ACTS)}"
            )

    # capture-ref reachability: every $name in set/reply/remember must be producible
    used_caps: set[str] = set()
    for sa in sets:
        if sa.is_capture_ref:
            used_caps.add(sa.value[1:])
        elif sa.op == "put" and isinstance(sa.value, list):
            for el in sa.value:
                if isinstance(el, str) and el.startswith("$") and not el.startswith("$$"):
                    used_caps.add(el[1:])
    if reply is not None:
        refs = check_template(reply, where)
        used_caps |= refs.captures
    if remember is not None:
        used_caps.add(remember[1:])
    unreachable = used_caps - produced
    if unreachable:
        raise ScriptError(
            f"{where}: capture ref(s) {sorted('$' + c for c in unreachable)} "
            f"not producible by this transition's matcher"
        )

    return Transition(
        matcher=matcher, when=when, sets=sets,
        goto=goto, push=push, pop=pop, reply=reply,
        affect=affect, remember=remember,
        topic=topic, cooldown=cooldown, once=once, key=key, act=act,
    )


# ----------------------------------------------------------------------------
# State + graph loading and validation.
# ----------------------------------------------------------------------------

@dataclass
class LoadedScript:
    states: dict[str, State]
    start: str
    intents: dict[str, ParsedIntent]
    personality: dict[str, Any] = field(default_factory=dict)
    personalities: dict[str, Any] = field(default_factory=dict)  # named presets (nice/feral/...)
    modes: dict[str, Any] = field(default_factory=dict)
    affect_states: list[str] = field(default_factory=list)
    # Shared grammar productions (from `include:` files) reachable from every state's
    # #symbol# expansion. Used to bring in the large imported response pools.
    global_grammar: dict[str, Any] = field(default_factory=dict)


def _parse_state(name: str, raw: dict, intents: dict[str, ParsedIntent], modes: dict | None = None,
                 global_grammar: dict | None = None) -> State:
    known = {"on_enter", "transitions", "fallback", "end", "grammar", "mode_grammar"}
    unknown = set(raw.keys()) - known
    if unknown:
        raise ScriptError(f"state {name}: unknown key(s) {sorted(unknown)}")

    global_grammar = global_grammar or {}
    grammar = raw.get("grammar", {}) or {}
    mode_grammar = raw.get("mode_grammar", {}) or {}
    if grammar:
        from .grammar import validate_grammar
        # validate baseline (+ global pools as a resolution backstop), and each mode layer
        validate_grammar({**global_grammar, **grammar}, f"state {name} grammar")
        known_modes = set((modes or {}).keys()) | {"NEUTRAL"}
        for mode_name, overrides in mode_grammar.items():
            if mode_name not in known_modes:
                raise ScriptError(f"state {name} mode_grammar: unknown mode {mode_name!r}")
            for sym in overrides:
                if sym not in grammar:
                    raise ScriptError(f"state {name} mode_grammar[{mode_name}]: symbol {sym!r} not in baseline grammar")
            # Merge global pools too: at runtime _resolve_grammar sees {global, local, overrides},
            # so a baseline symbol that references a global pool must still resolve here.
            merged = {**global_grammar, **grammar, **overrides}
            validate_grammar(merged, f"state {name} mode_grammar[{mode_name}]")

    end = bool(raw.get("end", False))
    on_enter = raw.get("on_enter")
    if on_enter is not None:
        check_template(on_enter, f"state {name} on_enter")

    if end:
        if "transitions" in raw or "fallback" in raw:
            raise ScriptError(f"state {name}: terminal (end: true) must have no transitions/fallback")
        _check_state_symbols(name, on_enter, (), None, grammar, global_grammar)
        return State(name, on_enter=on_enter, end=True,
                     grammar=grammar, mode_grammar=mode_grammar)

    transitions = tuple(
        parse_transition(t, f"state {name} transition[{i}]", intents, key=f"{name}#{i}")
        for i, t in enumerate(raw.get("transitions", []))
    )
    fallback = None
    if "fallback" in raw:
        fallback = parse_transition(raw["fallback"], f"state {name} fallback", intents,
                                    is_fallback=True, key=f"{name}#fallback")

    _check_state_symbols(name, on_enter, transitions, fallback, grammar, global_grammar)
    return State(
        name, on_enter=on_enter, transitions=transitions, fallback=fallback, end=False,
        grammar=grammar, mode_grammar=mode_grammar,
    )


_TEMPLATE_SYMBOL_RE = re.compile(r"#([a-zA-Z_][\w:]*)(?:\|[a-z]+)?#")


def _check_state_symbols(name: str, on_enter, transitions, fallback, grammar: dict,
                         global_grammar: dict | None = None) -> None:
    """Every #symbol# in this state's on_enter/reply templates must resolve.

    Resolves against the state's baseline grammar, the shared global pools, or the closed
    fetch vocabulary. A symbol matching none of those is a load error — the reference
    would expand to nothing at runtime.
    """
    from .grammar import _is_fetch

    resolvable = set(grammar) | set(global_grammar or {})
    templates: list[str] = []
    if on_enter:
        templates.append(on_enter)
    edges = list(transitions) + ([fallback] if fallback else [])
    for t in edges:
        if t.reply:
            templates.append(t.reply)

    for tmpl in templates:
        for sym in _TEMPLATE_SYMBOL_RE.findall(tmpl):
            if _is_fetch(sym):
                continue
            if sym not in resolvable:
                raise ScriptError(
                    f"state {name}: template references #{sym}# but it's not defined in "
                    f"this state's grammar, the global pools, or as a fetch function"
                )


def load(path: str | Path) -> LoadedScript:
    data = yaml.load(Path(path).read_text(encoding="utf-8"), Loader=_ElaineLoader)
    if not isinstance(data, dict):
        raise ScriptError("script root must be a mapping")

    start = data.get("start")
    if not start:
        raise ScriptError("script must declare a `start` state")

    intents = parse_intents(data.get("intents"))
    modes = data.get("modes", {}) or {}
    # States entered via affect routing (e.g. COOLDOWN at max anger), not a goto/push edge.
    # The engine's affect layer jumps to these; the graph validator treats them as roots.
    affect_states = list(data.get("affect_states", []) or [])

    # `include:` pulls in shared grammar pools (e.g. the imported sonarr response pools).
    # Each included file has a top-level `pools:` (or `grammar:`) mapping merged globally.
    base_dir = Path(path).parent
    global_grammar: dict[str, Any] = {}
    for inc in (data.get("include", []) or []):
        inc_path = (base_dir / inc) if not Path(inc).is_absolute() else Path(inc)
        inc_data = yaml.load(inc_path.read_text(encoding="utf-8"), Loader=_ElaineLoader)
        if not isinstance(inc_data, dict):
            raise ScriptError(f"include {inc!r}: root must be a mapping")
        block = inc_data.get("pools") or inc_data.get("grammar") or {}
        for sym, prods in block.items():
            global_grammar[sym] = prods

    raw_states = data.get("states") or {}
    if not raw_states:
        raise ScriptError("script must declare at least one state")

    # duplicate names: YAML mappings can't truly duplicate, but guard anyway via list check
    states: dict[str, State] = {}
    for sname, sraw in raw_states.items():
        if sname in states:
            raise ScriptError(f"duplicate state name {sname!r}")
        states[sname] = _parse_state(sname, sraw, intents, modes, global_grammar)

    for a in affect_states:
        if a not in states:
            raise ScriptError(f"affect_states references unknown state {a!r}")

    _validate_graph(states, start, affect_states)
    return LoadedScript(
        states=states, start=start, intents=intents,
        personality=data.get("personality", {}),
        personalities=data.get("personalities", {}) or {},
        modes=modes, affect_states=affect_states, global_grammar=global_grammar,
    )


def _deep_merge(base: dict, over: dict) -> dict:
    out = dict(base)
    for k, v in over.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = {**out[k], **v}
        else:
            out[k] = v
    return out


def build_personality(loaded: "LoadedScript", preset: str | None = None):
    """Build a Personality from the base personality block, optionally merged with a preset."""
    from .affect import Personality
    cfg = dict(loaded.personality or {})
    if preset:
        presets = loaded.personalities or {}
        if preset not in presets:
            raise ScriptError(f"unknown personality preset {preset!r}; available: {sorted(presets)}")
        cfg = _deep_merge(cfg, presets[preset] or {})
    return Personality.from_config(cfg)


def _targets(t: Transition) -> list[str]:
    out = []
    if t.goto is not None:
        out.append(t.goto)
    if t.push is not None:
        out.append(t.push)
    return out


def _validate_graph(states: dict[str, State], start: str, affect_states: list[str] | None = None) -> None:
    if start not in states:
        raise ScriptError(f"start state {start!r} does not exist")
    if states[start].end:
        raise ScriptError(f"start state {start!r} is terminal (zero-turn conversation)")

    # dangling goto/push targets; push into terminal
    for s in states.values():
        all_t = list(s.transitions) + ([s.fallback] if s.fallback else [])
        for t in all_t:
            for tgt in _targets(t):
                if tgt not in states:
                    raise ScriptError(f"state {s.name}: transition targets unknown state {tgt!r}")
            if t.push is not None and states[t.push].end:
                raise ScriptError(f"state {s.name}: push into terminal state {t.push!r} strands the return point")

    # reachability from start (follow goto + push), plus affect-routed entry states
    seen = set()
    stack = [start] + list(affect_states or [])
    while stack:
        cur = stack.pop()
        if cur in seen:
            continue
        seen.add(cur)
        s = states[cur]
        for t in list(s.transitions) + ([s.fallback] if s.fallback else []):
            stack.extend(_targets(t))
    unreachable = set(states) - seen
    if unreachable:
        raise ScriptError(f"unreachable state(s): {sorted(unreachable)}")

    # every non-terminal turn produces output; stay-put needs a reply
    for s in states.values():
        if s.end:
            continue
        has_always = any(t.matcher is not None and isinstance(t.matcher, M.Always) for t in s.transitions)
        if s.fallback is None and not has_always:
            raise ScriptError(f"state {s.name}: non-terminal state needs a `fallback` or an `always` transition")
        for i, t in enumerate(s.transitions):
            if not t.changes_state() and (t.reply is None or t.reply == ""):
                raise ScriptError(
                    f"state {s.name} transition[{i}]: stays in-state but has no reply "
                    f"(on_enter won't re-fire to cover it)"
                )
        if s.fallback is not None and not s.fallback.changes_state():
            if s.fallback.reply is None or s.fallback.reply == "":
                raise ScriptError(f"state {s.name} fallback: stays in-state but has no reply")

    # at least one terminal reachable (warn-level: raise only if none AND no /quit note)
    if not any(states[n].end for n in seen):
        # Per spec this is a warning, not a hard error; surface via a stored attribute.
        pass
