"""The acceptance checker (ACCEPTANCE.md §Checker).

Reads acceptance.yaml — a flat list mirroring the MUST/SHOULD items — runs each `verify`
command, prints a table, and exits non-zero if any MUST fails. SHOULD failures warn only.
`--phase N` runs only that phase's gate.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

import yaml

# Leading VAR=value VAR2=value tokens — parsed into the subprocess env so the command
# works on both POSIX sh and Windows cmd (which doesn't understand the inline-env syntax).
_ENV_PREFIX_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)=(\S*)\s+")


def _split_env_prefix(command: str) -> tuple[dict[str, str], str]:
    env_overrides: dict[str, str] = {}
    while True:
        m = _ENV_PREFIX_RE.match(command)
        if not m:
            break
        env_overrides[m.group(1)] = m.group(2)
        command = command[m.end():]
    return env_overrides, command


def run_check(acceptance_path: str = "acceptance.yaml", phase: int | None = None) -> int:
    path = Path(acceptance_path)
    if not path.exists():
        print(f"acceptance file not found: {acceptance_path}", file=sys.stderr)
        return 2
    items = yaml.safe_load(path.read_text(encoding="utf-8")) or []
    if phase is not None:
        items = [i for i in items if i.get("phase") == phase]

    rows = []
    must_failed = False
    for item in items:
        verify = item.get("verify")
        level = item.get("level", "MUST")
        if not verify:
            status = "SKIP"
        else:
            env_overrides, cmd = _split_env_prefix(verify)
            env = {**os.environ, **env_overrides} if env_overrides else None
            proc = subprocess.run(cmd, shell=True, capture_output=True, text=True, env=env)
            status = "PASS" if proc.returncode == 0 else "FAIL"
        if status == "FAIL" and level == "MUST":
            must_failed = True
        rows.append((item.get("id", "?"), item.get("phase", "?"), level, status, item.get("desc", "")))

    width = max((len(str(r[4])) for r in rows), default=0)
    print(f"{'id':<5} {'phase':<6} {'level':<7} {'status':<6} desc")
    print("-" * (30 + width))
    for rid, ph, level, status, desc in rows:
        print(f"{rid:<5} {ph:<6} {level:<7} {status:<6} {desc}")

    n_pass = sum(1 for r in rows if r[3] == "PASS")
    n_fail = sum(1 for r in rows if r[3] == "FAIL")
    print(f"\n{n_pass} pass, {n_fail} fail, {len(rows)} total")
    return 1 if must_failed else 0
