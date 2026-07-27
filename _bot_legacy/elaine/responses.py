r"""Response template rendering (DESIGN §5.1, Plan Phase 3).

Grammar tokens:
  {slot}          memory lookup (persisted across turns)
  $name / $0      this turn's regex captures ($0 = whole match)
  {reflect:$cap}  pronoun-swapped capture
  |               top-level variety alternatives (turn-seeded RNG)

Escapes (the ONLY ones): {{ -> {, }} -> }, $$ -> $, \| -> |.

Substitution is SINGLE-PASS and NON-RECURSIVE: a slot/capture value containing { or $
is inserted verbatim, never re-parsed (no template injection via user-provided text).

Undefined {slot} or $capture RAISES (never renders "") — most cases caught at load.
"""
from __future__ import annotations

import random
import re

from .memory import Memory
from .normalize import reflect


class RenderError(Exception):
    pass


def split_alternatives(template: str) -> list[str]:
    """Split on top-level `|`, honoring the `\\|` escape. Escapes elsewhere are left intact."""
    parts: list[str] = []
    buf: list[str] = []
    i = 0
    n = len(template)
    while i < n:
        c = template[i]
        if c == "\\" and i + 1 < n and template[i + 1] == "|":
            buf.append("\\|")  # keep escaped; resolved later in _substitute
            i += 2
            continue
        if c == "|":
            parts.append("".join(buf))
            buf = []
            i += 1
            continue
        buf.append(c)
        i += 1
    parts.append("".join(buf))
    return parts


_CAPTURE_RE = re.compile(r"[A-Za-z_]\w*|0")


def _resolve_brace(body: str, memory: Memory, captures: dict[str, str]) -> str:
    """Resolve the inside of a {...} token: either {slot} or {reflect:$cap}."""
    if body.startswith("reflect:"):
        arg = body[len("reflect:") :]
        if not arg.startswith("$"):
            raise RenderError(f"reflect: expects a $capture, got {body!r}")
        return reflect(_resolve_capture(arg[1:], captures))
    # plain slot
    if not memory.has(body):
        raise RenderError(f"undefined slot {{{body}}} (not set this conversation)")
    return str(memory.get(body))


def _resolve_capture(name: str, captures: dict[str, str]) -> str:
    if name not in captures:
        raise RenderError(
            f"undefined capture ${name} (this turn's match produced no such group)"
        )
    return captures[name]


def _substitute(template: str, memory: Memory, captures: dict[str, str]) -> str:
    """Single-pass, non-recursive substitution of one alternative (no top-level | here)."""
    out: list[str] = []
    i = 0
    n = len(template)
    while i < n:
        c = template[i]
        # escapes
        if c == "{" and i + 1 < n and template[i + 1] == "{":
            out.append("{"); i += 2; continue
        if c == "}" and i + 1 < n and template[i + 1] == "}":
            out.append("}"); i += 2; continue
        if c == "$" and i + 1 < n and template[i + 1] == "$":
            out.append("$"); i += 2; continue
        if c == "\\" and i + 1 < n and template[i + 1] == "|":
            out.append("|"); i += 2; continue
        # {slot} / {reflect:$cap}
        if c == "{":
            close = template.find("}", i + 1)
            if close == -1:
                raise RenderError(f"unbalanced '{{' in template: {template!r}")
            body = template[i + 1 : close]
            out.append(_resolve_brace(body, memory, captures))
            i = close + 1
            continue
        # $name / $0
        if c == "$":
            m = _CAPTURE_RE.match(template, i + 1)
            if not m:
                raise RenderError(f"malformed capture ref at {i} in {template!r}")
            out.append(_resolve_capture(m.group(0), captures))
            i = m.end()
            continue
        out.append(c)
        i += 1
    return "".join(out)


def render(template: str, memory: Memory, captures: dict[str, str], turn: int, salt: int = 0) -> str:
    """Render a template: pick a |-alternative (turn-seeded), then substitute.

    The RNG is seeded from (turn ^ salt) so two pieces rendered in one turn (a transition
    reply and a target on_enter) vary independently yet reproducibly (DESIGN §5.1).
    """
    alts = split_alternatives(template)
    if len(alts) == 1:
        chosen = alts[0]
    else:
        rng = random.Random(turn ^ salt)
        chosen = rng.choice(alts)
    return _substitute(chosen, memory, captures)
