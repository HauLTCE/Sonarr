"""Matchers: keyword / regex / intent / always, plus opt-in fuzzy keyword.

Every matcher returns a MatchResult(matched, captures). Captures come ONLY from regex
named groups, sliced from the original-case `cased` form (DESIGN §4). Keyword/fuzzy/
intent-non-regex/always contribute no captures.

Fuzzy keyword (enhancement #3, 2026-06-30): opt-in per matcher with an explicit max
edit distance. Deterministic (pure Levenshtein), so replay still holds. Contributes no
captures, exactly like exact keyword matching.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Protocol

from .normalize import Normalized, tokenize


@dataclass(frozen=True)
class MatchResult:
    matched: bool
    captures: dict[str, str] = field(default_factory=dict)


FALSE = MatchResult(False, {})


class Matcher(Protocol):
    def __call__(self, norm: Normalized) -> MatchResult: ...


@dataclass(frozen=True)
class Keyword:
    """Matches if any token (punctuation-stripped) is in the word set. No captures."""

    words: frozenset[str]

    def __call__(self, norm: Normalized) -> MatchResult:
        toks = set(tokenize(norm.cased))
        return MatchResult(True) if (toks & self.words) else FALSE


def keyword(words) -> Keyword:
    # str() coerces YAML 1.1 truthy tokens (yes/no/on/off parsed as bool) back to text.
    return Keyword(frozenset(str(w).lower() for w in words))


def _levenshtein(a: str, b: str, cap: int) -> int:
    """Edit distance with early exit once the best possible exceeds cap. Deterministic."""
    if abs(len(a) - len(b)) > cap:
        return cap + 1
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        row_min = i
        for j, cb in enumerate(b, 1):
            cost = 0 if ca == cb else 1
            v = min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost)
            cur.append(v)
            if v < row_min:
                row_min = v
        if row_min > cap:
            return cap + 1
        prev = cur
    return prev[-1]


@dataclass(frozen=True)
class FuzzyKeyword:
    """Matches if any token is within `max_distance` edits of a word. No captures.

    Opt-in only (enhancement #3). A short min_len guards against tiny tokens fuzzing
    into everything (e.g. distance-1 from "no" to "go").
    """

    words: frozenset[str]
    max_distance: int = 1
    min_len: int = 4

    def __call__(self, norm: Normalized) -> MatchResult:
        for tok in tokenize(norm.cased):
            if len(tok) < self.min_len:
                if tok in self.words:
                    return MatchResult(True)
                continue
            for w in self.words:
                if _levenshtein(tok, w, self.max_distance) <= self.max_distance:
                    return MatchResult(True)
        return FALSE


def fuzzy_keyword(words, max_distance: int = 1, min_len: int = 4) -> FuzzyKeyword:
    return FuzzyKeyword(frozenset(str(w).lower() for w in words), max_distance, min_len)


@dataclass(frozen=True)
class Regex:
    """re.search on the lower form; named-group captures sliced from cased at the span."""

    pattern: re.Pattern
    source: str

    def __call__(self, norm: Normalized) -> MatchResult:
        m = self.pattern.search(norm.lower)
        if not m:
            return FALSE
        captures: dict[str, str] = {}
        # $0 = whole match, sliced from cased to preserve original case.
        captures["0"] = norm.cased[m.start() : m.end()]
        for name, idx in self.pattern.groupindex.items():
            span = m.span(idx)
            if span == (-1, -1):
                continue
            captures[name] = norm.cased[span[0] : span[1]]
        return MatchResult(True, captures)


def regex(pattern: str) -> Regex:
    """Compile once at load. Patterns authored lowercase; no implicit flags (DESIGN §4)."""
    return Regex(re.compile(pattern), pattern)


@dataclass(frozen=True)
class Intent:
    """A named bundle; matches if ANY member matches. First matching member's captures win.

    Members tried in declared order. Keyword/fuzzy members contribute no captures, so an
    intent whose regex member has named groups can still feed set/reply.
    """

    name: str
    members: tuple[Matcher, ...]

    def __call__(self, norm: Normalized) -> MatchResult:
        for member in self.members:
            r = member(norm)
            if r.matched:
                return r
        return FALSE


def intent(name: str, *members: Matcher) -> Intent:
    return Intent(name, tuple(members))


@dataclass(frozen=True)
class Always:
    """The fallback matcher — always true, no captures."""

    def __call__(self, norm: Normalized) -> MatchResult:
        return MatchResult(True)


def always() -> Always:
    return Always()


@dataclass(frozen=True)
class Caps:
    """Matches SHOUTING: at least `min_letters` letters and an uppercase ratio >= min_ratio.

    Reads norm.cased (the case-preserving form) because norm.lower has erased the case
    signal. Contributes no captures. Lets the graph react to being yelled at.
    """

    min_letters: int = 3
    min_ratio: float = 0.7

    def __call__(self, norm: Normalized) -> MatchResult:
        letters = [c for c in norm.cased if c.isalpha()]
        if len(letters) < self.min_letters:
            return FALSE
        uppers = sum(1 for c in letters if c.isupper())
        return MatchResult(True) if (uppers / len(letters)) >= self.min_ratio else FALSE


def caps(min_letters: int = 3, min_ratio: float = 0.7) -> Caps:
    return Caps(min_letters, min_ratio)


_NUMBER_RE = re.compile(r"-?\d+(?:\.\d+)?")
# Emoji + common pictographic/symbol/arrow/dingbat ranges.
_EMOJI_RE = re.compile(
    "[\U0001F000-\U0001FAFF\U00002600-\U000027BF\U00002190-\U000021FF"
    "\U00002B00-\U00002BFF\U0000FE00-\U0000FE0F\U0001F1E6-\U0001F1FF]"
)
_ELONG_RE = re.compile(r"(.)\1\1")  # any char repeated 3+ times (sooo, !!!, ...)
_URL_RE = re.compile(r"https?://|www\.\w|\w+\.(?:com|net|org|io|gg)\b")
_MENTION_RE = re.compile(r"@\w+")


@dataclass(frozen=True)
class Number:
    """Matches if the input contains a number; captures the first as $number and $0."""

    def __call__(self, norm: Normalized) -> MatchResult:
        m = _NUMBER_RE.search(norm.lower)
        if not m:
            return FALSE
        val = norm.cased[m.start():m.end()]
        return MatchResult(True, {"0": val, "number": val})


def number() -> Number:
    return Number()


@dataclass(frozen=True)
class StartsWith:
    prefixes: tuple[str, ...]

    def __call__(self, norm: Normalized) -> MatchResult:
        low = norm.lower.lstrip()
        return MatchResult(True) if any(low.startswith(p) for p in self.prefixes) else FALSE


def starts_with(words) -> StartsWith:
    return StartsWith(tuple(str(w).lower() for w in words))


@dataclass(frozen=True)
class EndsWith:
    suffixes: tuple[str, ...]

    def __call__(self, norm: Normalized) -> MatchResult:
        low = norm.lower.rstrip()
        return MatchResult(True) if any(low.endswith(s) for s in self.suffixes) else FALSE


def ends_with(words) -> EndsWith:
    return EndsWith(tuple(str(w).lower() for w in words))


@dataclass(frozen=True)
class AllKeywords:
    """Matches only if every listed keyword is present (AND, vs Keyword's OR)."""

    words: frozenset[str]

    def __call__(self, norm: Normalized) -> MatchResult:
        toks = set(tokenize(norm.cased))
        return MatchResult(True) if self.words <= toks else FALSE


def all_keywords(words) -> AllKeywords:
    return AllKeywords(frozenset(str(w).lower() for w in words))


@dataclass(frozen=True)
class Length:
    """Matches inputs within word/char bounds — e.g. one-word or wall-of-text."""

    min_words: int = 0
    max_words: int = 1_000_000
    min_chars: int = 0
    max_chars: int = 1_000_000

    def __call__(self, norm: Normalized) -> MatchResult:
        nwords = len(norm.cased.split())
        nchars = len(norm.cased)
        ok = (self.min_words <= nwords <= self.max_words
              and self.min_chars <= nchars <= self.max_chars)
        return MatchResult(True) if ok else FALSE


def length(min_words=0, max_words=1_000_000, min_chars=0, max_chars=1_000_000) -> Length:
    return Length(min_words, max_words, min_chars, max_chars)


@dataclass(frozen=True)
class Emoji:
    """Matches if the input contains at least one emoji/pictographic symbol."""

    def __call__(self, norm: Normalized) -> MatchResult:
        return MatchResult(True) if _EMOJI_RE.search(norm.lower) else FALSE


def emoji() -> Emoji:
    return Emoji()


@dataclass(frozen=True)
class Elongated:
    """Matches drawn-out text: a character repeated 3+ times (sooo, ahhh, !!!)."""

    def __call__(self, norm: Normalized) -> MatchResult:
        return MatchResult(True) if _ELONG_RE.search(norm.lower) else FALSE


def elongated() -> Elongated:
    return Elongated()


@dataclass(frozen=True)
class Url:
    """Matches if the input looks like it contains a link."""

    def __call__(self, norm: Normalized) -> MatchResult:
        return MatchResult(True) if _URL_RE.search(norm.lower) else FALSE


def url() -> Url:
    return Url()


@dataclass(frozen=True)
class Mention:
    """Matches an @mention token."""

    def __call__(self, norm: Normalized) -> MatchResult:
        return MatchResult(True) if _MENTION_RE.search(norm.lower) else FALSE


def mention() -> Mention:
    return Mention()
