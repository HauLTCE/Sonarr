"""Episodic memory — the quote log + fetch functions (AFFECT_MODEL §6, GRAMMAR §3).

A per-user append-only log of captured quotes, each tagged with a logical timestamp and
the sentiment under which it was said. The grammar reads it via fetch functions (recall/
contradiction/mood-congruent). Elaine never understands a quote — she knows when to throw
one back (a mode/condition) and which slot to fetch. The text is opaque sliced input.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from .grammar import FetchContext


@dataclass
class Quote:
    turn: int
    text: str
    tag: str  # sentiment label under which it was said (HOSTILE/FRIENDLY/NEUTRAL)


@dataclass
class EpisodicLog:
    quotes: list[Quote] = field(default_factory=list)
    cap: int = 50

    def add(self, turn: int, text: str, tag: str) -> None:
        self.quotes.append(Quote(turn, text, tag))
        if len(self.quotes) > self.cap:
            # prune oldest, keep most recent `cap`
            self.quotes = self.quotes[-self.cap :]

    def by_tag(self, tag: str) -> list[Quote]:
        return [q for q in self.quotes if q.tag == tag]

    def has_tags(self, *tags: str) -> bool:
        present = {q.tag for q in self.quotes}
        return all(t in present for t in tags)

    def to_list(self) -> list[dict[str, Any]]:
        return [{"turn": q.turn, "text": q.text, "tag": q.tag} for q in self.quotes]

    @classmethod
    def from_list(cls, data: list[dict[str, Any]], cap: int = 50) -> "EpisodicLog":
        log = cls(cap=cap)
        log.quotes = [Quote(d["turn"], d["text"], d["tag"]) for d in (data or [])]
        return log


# Tags considered "worst" / "nicest" for mood-congruent recall.
_WORST = "HOSTILE"
_NICEST = "FRIENDLY"


@dataclass
class AffectFetchContext:
    """A FetchContext (for grammar.expand) backed by registers + episodic log."""

    log: EpisodicLog
    name_slot: str | None
    mode_name: str
    counts: dict[str, int] = field(default_factory=dict)
    mood_congruent: bool = True  # when SEETHING fetch worst; when FOND fetch nicest
    turn: int = 0                # logical clock, exposed to grammar via #turns#
    facts: dict = field(default_factory=dict)   # structured facts (#fact:key#)
    topic: str | None = None                    # current conversation topic (#topic#)
    daypart: str | None = None                  # adapter-supplied time-of-day (#daypart#)
    topics_seen: list = field(default_factory=list)  # topic history (#recap#)

    def recall(self, tag: str) -> str | None:
        # Mood-congruent: in a hostile mode prefer the worst quote of that tag; default newest.
        quotes = self.log.by_tag(tag)
        if not quotes:
            return None
        return quotes[-1].text  # most recent of that tag

    def name(self) -> str | None:
        return self.name_slot

    def count(self, register: str) -> int:
        return int(self.counts.get(register, 0))

    def mode(self) -> str:
        return self.mode_name

    def turns(self) -> int:
        return int(self.turn)

    def get_topic(self) -> str:
        return self.topic or "whatever we were on"

    def get_daypart(self) -> str:
        return self.daypart or "day"

    def fact(self, key: str) -> str:
        v = (self.facts or {}).get(key)
        return str(v) if v else "a mystery"

    def recap(self) -> str:
        parts: list[str] = []
        seen = list(dict.fromkeys(self.topics_seen or []))
        if seen:
            parts.append("we touched on " + ", ".join(seen[-6:]))
        if self.facts:
            parts.append("; ".join(f"your {k} is {v}" for k, v in list(self.facts.items())[:4]))
        return ". ".join(parts) if parts else "nothing worth recapping, honestly"


def has_contradiction(log: EpisodicLog) -> bool:
    """True if the log holds both a FRIENDLY and a HOSTILE quote (hypocrisy fragment)."""
    return log.has_tags(_NICEST, _WORST)
