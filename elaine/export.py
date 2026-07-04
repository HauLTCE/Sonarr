"""Extra graph export formats: Mermaid and JSON (complements graphviz_export.to_dot).

Same graph the .dot export renders, in formats that embed in Markdown (Mermaid) or feed
other tooling (JSON). Pure, deterministic, no engine state.
"""
from __future__ import annotations

import json

from .matchers import Always, Caps, FuzzyKeyword, Intent, Keyword, Regex
from .script import LoadedScript
from .state import Transition


def _matcher_label(t: Transition) -> str:
    m = t.matcher
    if m is None:
        return "when"
    if isinstance(m, Always):
        return "*"
    if isinstance(m, Keyword):
        return "kw:" + "/".join(sorted(m.words))[:20]
    if isinstance(m, FuzzyKeyword):
        return "fuzzy:" + "/".join(sorted(m.words))[:16]
    if isinstance(m, Regex):
        return "re:" + m.source[:20]
    if isinstance(m, Intent):
        return "intent:" + m.name
    if isinstance(m, Caps):
        return "caps"
    return type(m).__name__


def _edge_target(name: str, t: Transition) -> tuple[str, str]:
    """Return (target_state, kind) where kind in goto/push/pop/stay."""
    if t.push is not None:
        return t.push, "push"
    if t.pop:
        return name, "pop"
    if t.goto is not None:
        return t.goto, "goto"
    return name, "stay"


def _safe_id(name: str) -> str:
    return "".join(c if c.isalnum() else "_" for c in name)


def to_mermaid(loaded: LoadedScript) -> str:
    """A Mermaid `flowchart LR` — pastes straight into Markdown/GitHub."""
    lines = ["flowchart LR"]
    for name, state in loaded.states.items():
        nid = _safe_id(name)
        if state.end:
            lines.append(f"    {nid}(({name}))")      # terminal = double circle
        elif name == loaded.start:
            lines.append(f"    {nid}[/{name}/]")        # start = slanted
        else:
            lines.append(f"    {nid}[{name}]")
    for name, state in loaded.states.items():
        edges = list(state.transitions) + ([state.fallback] if state.fallback else [])
        for t in edges:
            target, kind = _edge_target(name, t)
            label = _matcher_label(t).replace('"', "'")
            arrow = "-.->" if kind in ("push", "pop") else "-->"
            lines.append(f'    {_safe_id(name)} {arrow}|"{label}"| {_safe_id(target)}')
    return "\n".join(lines)


def to_json(loaded: LoadedScript, indent: int = 2) -> str:
    """A machine-readable dump of the graph: states, transitions, intents, modes."""
    states = {}
    for name, state in loaded.states.items():
        edges = []
        all_t = list(state.transitions) + ([state.fallback] if state.fallback else [])
        for i, t in enumerate(all_t):
            target, kind = _edge_target(name, t)
            edges.append({
                "index": i if i < len(state.transitions) else "fallback",
                "matcher": _matcher_label(t),
                "kind": kind,
                "target": target,
                "has_when": t.when is not None,
                "has_reply": bool(t.reply),
                "affect": {k: v for k, v in t.affect},
            })
        states[name] = {
            "start": name == loaded.start,
            "terminal": state.end,
            "on_enter": state.on_enter,
            "grammar_symbols": sorted((state.grammar or {}).keys()),
            "transitions": edges,
        }
    doc = {
        "start": loaded.start,
        "states": states,
        "intents": sorted(loaded.intents.keys()),
        "modes": list((loaded.modes or {}).keys()),
        "affect_states": list(loaded.affect_states or []),
        "pools": len(loaded.global_grammar or {}),
    }
    return json.dumps(doc, indent=indent)
