"""Graphviz .dot export of a loaded script (Plan Phase 6).

Renders the dialogue graph so the "mind" is visualizable: nodes are states (terminal
states double-circled), edges are transitions labeled by their matcher, dashed for push,
dotted for pop.
"""
from __future__ import annotations

from .matchers import Always, FuzzyKeyword, Intent, Keyword, Regex
from .script import LoadedScript
from .state import Transition


def _matcher_label(t: Transition) -> str:
    m = t.matcher
    if m is None:
        return "when"
    if isinstance(m, Always):
        return "*"
    if isinstance(m, Keyword):
        return "kw:" + "/".join(sorted(m.words))[:24]
    if isinstance(m, FuzzyKeyword):
        return "fuzzy:" + "/".join(sorted(m.words))[:20]
    if isinstance(m, Regex):
        return "re:" + m.source[:24]
    if isinstance(m, Intent):
        return "intent:" + m.name
    return type(m).__name__


def _escape(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"')


def to_dot(loaded: LoadedScript) -> str:
    lines = ["digraph elaine {", "  rankdir=LR;", '  node [shape=ellipse];']
    for name, state in loaded.states.items():
        attrs = []
        if state.end:
            attrs.append("shape=doublecircle")
        if name == loaded.start:
            attrs.append("style=filled")
            attrs.append("fillcolor=lightblue")
        attr_str = f" [{', '.join(attrs)}]" if attrs else ""
        lines.append(f'  "{_escape(name)}"{attr_str};')

    for name, state in loaded.states.items():
        edges = list(state.transitions)
        if state.fallback is not None:
            edges.append(state.fallback)
        for t in edges:
            label = _matcher_label(t)
            style = ""
            if t.push is not None:
                target = t.push
                style = ' style=dashed'
            elif t.pop:
                target = "(pop)"
                style = ' style=dotted'
            elif t.goto is not None:
                target = t.goto
            else:
                target = name  # stays put
            lines.append(f'  "{_escape(name)}" -> "{_escape(target)}" [label="{_escape(label)}"{style}];')
    lines.append("}")
    return "\n".join(lines)
