"""Text normalization: cased/lower forms, tokenization, reflection.

The central invariant (DESIGN §4): `lower` is the *length-preserving* per-character
lowercasing of `cased`, so a match span found on `lower` slices the correct substring
from `cased`. Never use str.lower()/casefold() on the whole string — they can change
length (İ→i̇, ß→ss) and desync every downstream capture offset.
"""
from __future__ import annotations

import re
from dataclasses import dataclass

# Trailing punctuation stripped from the whole normalized string (DESIGN §4).
_TRAILING_PUNCT = ".!?,;:"

# Unicode punctuation unification: map "smart"/typographic characters to their ASCII
# equivalents so "don’t"/"don't" and "cool—" match the same rules. STRICTLY 1:1 (each key
# maps to a single char) so the length-preserving cased/lower invariant is never violated.
_UNIFY = str.maketrans({
    "\u201c": '"', "\u201d": '"', "\u201e": '"', "\u201f": '"',   # curly double quotes
    "\u2018": "'", "\u2019": "'", "\u201a": "'", "\u201b": "'",    # curly single quotes / apostrophe
    "\u2032": "'", "\u2033": '"',                                   # prime / double prime
    "\u2013": "-", "\u2014": "-", "\u2015": "-", "\u2212": "-",     # en/em dash, minus sign
    "\u00a0": " ", "\u2007": " ", "\u202f": " ", "\u200b": " ",     # nbsp / figure / narrow / zero-width
})

# Per-token leading/trailing strip set for keyword tokenization. Interior kept ("don't").
_TOKEN_STRIP = ".,!?;:\"'()[]{}<>"

# ELIZA-style pronoun swap. Applied token-wise, never as substring (DESIGN §4 / Plan P1).
_REFLECT = {
    "i": "you",
    "me": "you",
    "my": "your",
    "mine": "yours",
    "myself": "yourself",
    "am": "are",
    "you": "i",
    "your": "my",
    "yours": "mine",
    "yourself": "myself",
    "are": "am",
}


@dataclass(frozen=True)
class Normalized:
    """One canonical cased string and its length-preserving lowercasing.

    cased and lower are character-for-character index-aligned (differ only by case),
    so a span [i:j] found on `lower` slices the matching substring from `cased`.
    """

    cased: str
    lower: str

    def __post_init__(self) -> None:
        assert len(self.lower) == len(self.cased), (
            "lower must be length-preserving (index-aligned with cased)"
        )


def _length_preserving_lower(s: str) -> str:
    """Lowercase per character, keeping the original char when c.lower() isn't exactly one char.

    Guarantees len(out) == len(s). A char whose lowercasing would expand (İ→i̇) or
    that has no single-char lower form is left unchanged — a deterministic trade that
    never corrupts a capture span. Do NOT use casefold() (ß→ss) on this path.
    """
    out = []
    for c in s:
        lc = c.lower()
        out.append(lc if len(lc) == 1 else c)
    return "".join(out)


def normalize(text: str) -> Normalized:
    """Raw text → canonical (cased, lower) pair.

    Strips, collapses internal whitespace, drops trailing sentence punctuation.
    Whitespace collapsing happens on the single `cased` string *before* deriving
    `lower`, so the two never desync (the "my   name   is   Sam" trap).
    """
    cased = " ".join(text.translate(_UNIFY).strip().split())
    cased = cased.rstrip(_TRAILING_PUNCT).rstrip()
    lower = _length_preserving_lower(cased)
    return Normalized(cased=cased, lower=lower)


def tokenize(text: str) -> list[str]:
    """Split the lower form on whitespace, strip leading/trailing punctuation per token.

    Interior punctuation kept ("don't" survives). Used only by keyword matching, which
    contributes no captures — so this never touches the index-aligned cased/lower span.
    """
    lower = _length_preserving_lower(" ".join(text.strip().split()))
    tokens = []
    for raw in lower.split():
        t = raw.strip(_TOKEN_STRIP)
        if t:
            tokens.append(t)
    return tokens


_WORD_RE = re.compile(r"[^\W_]+", re.UNICODE)


def reflect(text: str) -> str:
    """ELIZA-style pronoun swap, applied word-wise so substrings aren't clobbered.

    "i hate my job" → "you hate your job"; "trim" must NOT become "tryou".
    A text transform used by responses.py, never a matcher (returns no boolean).
    """

    def swap(m: re.Match) -> str:
        w = m.group(0)
        repl = _REFLECT.get(w.lower())
        return repl if repl is not None else w

    return _WORD_RE.sub(swap, text)
