#!/usr/bin/env python3
"""Pull the chat turns out of a `journalctl -u sonarr` dump and drop everything else.

The legacy bot logs one line per Elaine exchange:

    2026-07-04T15:42:52+07:00 SONARR python[7263]: [Information] turn=14 CHAT->CHAT \
        sentiment=NEUTRAL mode=NEUTRAL fired=intent:GREETING | in='yo' out='something you need?'

Those lines are the only part of the journal worth keeping — the rest is Lavalink
reconnect spam, cache sweeps and extension loading. Output is JSONL, one turn per
line, so the .NET side can read it without a parser.

Usage: python scripts/extract-chat-turns.py IN.log OUT.jsonl

The output holds real user messages. training-data/ is gitignored; keep it there.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

# The quote character is whichever one Python's repr picked, and it differs between
# the `in` and `out` halves of the same line, so each side captures its own and
# back-references it. Non-greedy plus the anchored ` out=` keeps a quoted quote inside
# the message from ending the field early.
TURN = re.compile(
    r"^(?P<ts>\S+) \S+ \S+: \[\w+\] turn=(?P<turn>\d+) (?P<route>\S+) "
    r"sentiment=(?P<sentiment>\S+) mode=(?P<mode>\S+) fired=(?P<fired>\S+) \| "
    r"in=(?P<q1>['\"])(?P<inp>.*?)(?P=q1) out=(?P<q2>['\"])(?P<out>.*)(?P=q2)\s*$"
)


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2

    src, dst = Path(argv[1]), Path(argv[2])
    total = kept = 0

    with src.open(encoding="utf-8", errors="replace") as fh, dst.open(
        "w", encoding="utf-8", newline="\n"
    ) as out:
        for line in fh:
            total += 1
            m = TURN.match(line.rstrip("\n"))
            if not m:
                continue
            kept += 1
            out.write(
                json.dumps(
                    {
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

    print(f"read {total} lines, kept {kept} turns, dropped {total - kept}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
