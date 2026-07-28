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

Usage: python scripts/extract-chat-turns.py IN.log OUT.jsonl

The output holds real user messages. training-data/ is gitignored; keep it there.
"""

from __future__ import annotations

import json
import re
import sys
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


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    src, dst = Path(argv[1]), Path(argv[2])
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


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
