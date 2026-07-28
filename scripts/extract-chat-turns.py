#!/usr/bin/env python3
"""Pull the chat exchanges out of a `journalctl -u sonarr` dump and drop everything else.

The legacy bot logged exchanges in two formats over its life, and both are worth keeping.

The newer one is a single line per exchange:

    [Information] turn=14 CHAT->CHAT sentiment=NEUTRAL mode=NEUTRAL fired=intent:GREETING \
        | in='yo' out='something you need?'

The older one spans two or three lines — the classifier narrates its decision, then the
reply is logged with the category it resolved to:

    [Information] [Classify] Input: 'hello'
    [Information] [Classify] Fuzzy Keyword match: social_greeting (conf=2)
    [Information] [BotReply] Q: 'hello' (cat: social_greeting) -> A: 'Make it quick.'

`[BotReply]` is the anchor: it holds the input, the output and the category together. The
preceding `[Classify]` lines are folded onto that record, so a row carries not just the
pair but which tier decided it (fuzzy keyword, zero-shot, embedding, a BART/DeBERTa/RoBERTa
verifier, or nothing at all). That routing is the interesting part for tuning — it is the
old bot's version of the `fired=` field the newer format has.

Everything else in the journal is dropped: Lavalink reconnect spam, cache sweeps,
extension loading, tracebacks.

Three verbs:

    python scripts/extract-chat-turns.py extract IN.log OUT.jsonl
    python scripts/extract-chat-turns.py corpus OUT.jsonl IN.jsonl [IN.jsonl ...]
    python scripts/extract-chat-turns.py sessions OUT.jsonl IN.jsonl [IN.jsonl ...]

`sessions` rebuilds real conversations instead of single messages: one row per (channel,
person, run of messages under half an hour apart). That is what the multi-turn replay reads —
a single message cannot show state carried across turns, which is where this engine differs
from the old one most.

`corpus` folds the extracted files into one row per distinct user message — the input, how
often it was seen, the legacy routing when known, and the legacy reply for reference. That
is what the reply-quality suite reads: it tests the *new* engine's answer to each real
input, and the legacy reply is evidence of what was asked rather than a target to match.
The persona was migrated but the engine was rewritten, so asserting against the old output
would pin the old bot's behaviour.

Both outputs hold real user messages. training-data/ is gitignored; keep them there.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from datetime import datetime
from pathlib import Path

# The quote character is whichever one Python's repr picked, and it differs between the
# in= and out= halves of the same line, so each side captures its own and back-references
# it. Non-greedy plus the anchored ` out=` keeps a quoted quote inside a message from
# ending the field early.
TURN = re.compile(
    r"^(?P<ts>\S+) \S+ \S+: \[\w+\] turn=(?P<turn>\d+) (?P<route>\S+) "
    r"sentiment=(?P<sentiment>\S+) mode=(?P<mode>\S+) fired=(?P<fired>\S+) \| "
    r"in=(?P<q1>['\"])(?P<inp>.*?)(?P=q1) out=(?P<q2>['\"])(?P<out>.*)(?P=q2)\s*$"
)

# [BotReply] Q: 'define gay' (cat: request_action) -> A: 'I'm not fetching that for you.'
# The reply half is greedy to the final quote, because replies contain apostrophes far more
# often than they contain the literal "' (cat: " that would fool the non-greedy input half.
BOTREPLY = re.compile(
    r"^(?P<ts>\S+) \S+ \S+: \[\w+\] \[BotReply\] "
    r"Q: (?P<q1>['\"])(?P<inp>.*?)(?P=q1) \(cat: (?P<cat>[^)]*)\) -> "
    r"A: (?P<q2>['\"])(?P<out>.*)(?P=q2)\s*$"
)

# [Classify] Input: 'hello'   — opens a block, so it resets any half-built state.
CLASSIFY_INPUT = re.compile(
    r"\[Classify\] Input: (?P<q>['\"])(?P<inp>.*)(?P=q)\s*$"
)

# [Classify] Fuzzy Keyword match: social_greeting (conf=2)
# [Classify] VERIFIER CONSENSUS (BART, DeBERTa, RoBERTa): user_commanding
# Everything after the tag up to the first colon is the tier that decided; the rest is its
# detail. Kept as free text: there are 40-odd variants and no consumer parses them yet.
CLASSIFY_STEP = re.compile(
    r"\[Classify\] (?P<how>[^:]+): (?P<detail>.*?)\s*$"
)


# The mention prefix the context rows carry: '<@1452290540769906688> test' was typed as '@Sonarr
# test', and the id is noise to a reply engine that already knows it was addressed.
MENTION = re.compile(r"<@!?\d+>")

# A bare link is not a message the engine can answer, and it is the one field most likely to carry
# something personal. Same for a message that is only an emoji-and-punctuation husk after stripping.
URL_ONLY = re.compile(r"^\s*<?https?://\S+>?\s*$")

# Belt and braces: none of this is in the current dump, but the corpus is regenerated from future
# journals too, and a leaked invite or token in a gitignored file is still a leaked secret the
# moment someone pastes a review sheet somewhere.
SECRETS = (
    re.compile(r"(?:https?://)?(?:discord\.gg|discord(?:app)?\.com/invite)/\S+", re.I),
    re.compile(r"\b[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{6}\.[A-Za-z0-9_-]{27,}\b"),
)


def normalise(text: str) -> str:
    """The dedupe key: mentions gone, whitespace collapsed, case folded.

    Only the key is folded — the row keeps the original casing of the first occurrence, because
    the engine's SET_NAME intent reads the name out of the message verbatim.
    """
    return " ".join(MENTION.sub(" ", text).split()).casefold()


def clean(text: str) -> str | None:
    """The message as the engine should see it, or None if it is not a message at all."""
    for pattern in SECRETS:
        text = pattern.sub("[redacted]", text)

    text = " ".join(MENTION.sub(" ", text).split())

    if not text or URL_ONLY.match(text):
        return None

    return text


def corpus(argv: list[str]) -> int:
    """One row per distinct user message, merged across every extracted file given."""
    dst, srcs = Path(argv[0]), [Path(p) for p in argv[1:]]

    # Insertion-ordered, so the corpus reads in the order the messages were actually said. First
    # occurrence wins for casing and for the legacy reply; later ones only bump the count.
    rows: dict[str, dict[str, object]] = {}
    read = skipped = 0

    for src in srcs:
        with src.open(encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue

                read += 1
                rec = json.loads(line)

                # context rows are raw channel messages, so half of them are hers.
                if rec.get("is_bot"):
                    skipped += 1
                    continue

                text = clean(rec.get("in") or rec.get("content") or "")
                if text is None:
                    skipped += 1
                    continue

                key = normalise(text)
                row = rows.get(key)

                if row is None:
                    rows[key] = row = {
                        "in": text,
                        "seen": 0,
                        # Both are absent on input-only context rows, and `fired` is absent on the
                        # older format. A consumer must treat either as "unknown", never as "none".
                        **({"legacy_category": rec["category"]} if rec.get("category") else {}),
                        **({"legacy_fired": rec["fired"]} if rec.get("fired") else {}),
                        **({"legacy_out": rec["out"]} if rec.get("out") else {}),
                        "first_seen": rec.get("ts"),
                    }

                row["seen"] = int(row["seen"]) + 1  # type: ignore[arg-type]

    with dst.open("w", encoding="utf-8", newline="\n") as out:
        for row in rows.values():
            out.write(json.dumps(row, ensure_ascii=False) + "\n")

    paired = sum(1 for r in rows.values() if "legacy_out" in r)
    print(
        f"read {read} rows from {len(srcs)} file(s), wrote {len(rows)} distinct inputs "
        f"({paired} with a legacy reply, {len(rows) - paired} input-only), skipped {skipped}"
    )
    return 0


def extract(argv: list[str]) -> int:
    src, dst = Path(argv[0]), Path(argv[1])
    total = turns = replies = 0
    # The classifier state carried from the [Classify] lines onto the next [BotReply].
    pending_input: str | None = None
    steps: list[dict[str, str]] = []

    with src.open(encoding="utf-8", errors="replace") as fh, dst.open(
        "w", encoding="utf-8", newline="\n"
    ) as out:
        for raw in fh:
            total += 1
            line = raw.rstrip("\n")

            m = TURN.match(line)
            if m:
                turns += 1
                out.write(
                    json.dumps(
                        {
                            "kind": "turn",
                            "ts": m["ts"],
                            "turn": int(m["turn"]),
                            "route": m["route"],
                            "sentiment": m["sentiment"],
                            "mode": m["mode"],
                            "fired": m["fired"],
                            "in": m["inp"],
                            "out": m["out"],
                        },
                        ensure_ascii=False,
                    )
                    + "\n"
                )
                continue

            m = BOTREPLY.match(line)
            if m:
                replies += 1
                rec = {
                    "kind": "reply",
                    "ts": m["ts"],
                    "category": m["cat"],
                    "in": m["inp"],
                    "out": m["out"],
                }
                # Only trust the classifier trail if it was about this same input — a
                # dropped or interleaved line must not attribute another turn's routing.
                if pending_input == m["inp"] and steps:
                    rec["classify"] = steps
                out.write(json.dumps(rec, ensure_ascii=False) + "\n")
                pending_input, steps = None, []
                continue

            m = CLASSIFY_INPUT.search(line)
            if m:
                pending_input, steps = m["inp"], []
                continue

            m = CLASSIFY_STEP.search(line)
            if m and pending_input is not None:
                steps.append({"how": m["how"].strip(), "detail": m["detail"]})

    kept = turns + replies
    print(
        f"read {total} lines, kept {kept} exchanges "
        f"({turns} turn / {replies} reply), dropped {total - kept}"
    )
    return 0


def sessions(argv: list[str]) -> int:
    """Real conversations, rebuilt per (channel, person) from the context rows.

    A session is that person's messages in one channel, in the order they said them. Her side
    is dropped: the replay produces its own, and keeping the legacy reply as if it were part of
    the input would feed the old bot's words back into the new engine's state.

    A gap longer than GAP_MINUTES starts a new session. Two messages an hour apart are not a
    conversation, and stacking them onto one state would age registers that never aged.
    """
    dst, srcs = Path(argv[0]), [Path(p) for p in argv[1:]]

    # (channel, author) -> list of sessions, each a list of turns. Insertion-ordered.
    grouped: dict[tuple[str, str], list[list[dict[str, str]]]] = {}
    read = skipped = 0

    for src in srcs:
        with src.open(encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line:
                    continue

                read += 1
                rec = json.loads(line)

                # Only the context format carries who said it and where; the turn/reply formats
                # have no channel, so they cannot be sequenced into a conversation at all.
                if rec.get("is_bot") or not rec.get("channel_id") or not rec.get("author_id"):
                    skipped += 1
                    continue

                text = clean(rec.get("content") or "")
                if text is None:
                    skipped += 1
                    continue

                key = (str(rec["channel_id"]), str(rec["author_id"]))
                runs = grouped.setdefault(key, [[]])
                turn = {"in": text, "ts": rec.get("ts") or ""}

                if runs[-1] and _gap_minutes(runs[-1][-1]["ts"], turn["ts"]) > GAP_MINUTES:
                    runs.append([])

                runs[-1].append(turn)

    with dst.open("w", encoding="utf-8", newline="\n") as out:
        written = 0
        for (channel, author), runs in grouped.items():
            for index, turns in enumerate(runs):
                # A one-message session is already covered by the single-turn suite, and it
                # cannot show the thing this file exists to show: state carried across turns.
                if len(turns) < 2:
                    continue

                out.write(json.dumps({
                    # Both ids are hashed: the file is gitignored, but a session is a much
                    # sharper identifier than a lone message, and nothing downstream needs
                    # the real values -- only that two sessions are or are not the same person.
                    "session": f"{_tag(channel)}-{_tag(author)}-{index}",
                    "turns": turns,
                }, ensure_ascii=False) + "\n")
                written += 1

    print(
        f"read {read} rows from {len(srcs)} file(s), wrote {written} multi-turn sessions "
        f"from {len(grouped)} (channel, person) pairs, skipped {skipped}"
    )
    return 0


# Long enough that a pause to type or to read does not split a conversation, short enough that
# the next evening in the same channel is not glued onto this one.
GAP_MINUTES = 30.0


def _gap_minutes(before: str, after: str) -> float:
    """Minutes between two ISO timestamps, or 0.0 if either is missing or unparseable.

    Zero means "same session": an unknown gap is not evidence of a break, and guessing one
    would split conversations on a formatting change in the journal.
    """
    try:
        return abs(
            (datetime.fromisoformat(after) - datetime.fromisoformat(before)).total_seconds()
        ) / 60.0
    except ValueError:
        return 0.0


def _tag(value: str) -> str:
    """A short stable pseudonym for an id. Not a security boundary -- it removes the id from a
    file a human will read, and it survives regeneration so a finding stays findable."""
    return hashlib.sha256(value.encode()).hexdigest()[:8]


VERBS = {"extract": (extract, 2), "corpus": (corpus, 2), "sessions": (sessions, 2)}


def main(argv: list[str]) -> int:
    verb = VERBS.get(argv[1]) if len(argv) > 1 else None

    if verb is None or len(argv) - 2 < verb[1]:
        print(__doc__, file=sys.stderr)
        return 2

    return verb[0](argv[2:])


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
