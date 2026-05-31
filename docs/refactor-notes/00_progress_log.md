# Hardening Pass — Progress Log (2026-05-31)

Branch: `refactor/hardening-2026-05-31` (off `main` @ fd4bea7)
Working while you're resting. Every change is its own small commit so anything
can be rolled back with `git revert <sha>`. This log updates as I go.

## Ground rules I'm holding myself to
- **Music / Lavalink: read-only.** You said the cog is fine as long as I don't
  touch the Lavalink side or versions. I'll diagnose the now-playing issue and
  write the fix here for your approval. No music code changes without you.
- **High-risk work gets a plan, not a blind edit.** Full modularization and the
  shared-cursor change are documented for sign-off, not executed unsupervised.
- **Low-risk work I just do**: secrets→.env, unused imports, docstring/bug drift,
  dead code. All reversible, all committed separately.

## Status

| # | Task | Risk | State |
|---|------|------|-------|
| 1 | Secrets → .env | low | DONE (commit d12e1f3) |
| 2 | Clean unused imports (16) | low | DONE (884da58) |
| 3 | Games docstring drift + roulette aliases | low | DONE (ab22ee3) |
| 4 | Remove phantom Postgres backend (-121 lines) | low | DONE (b04779a) |
| 5 | Music now-playing diagnosis | read-only | DONE (doc 02) — needs your pick |
| 6 | Classifier tuning from real logs | read-only | DONE (doc 04) |
| 7 | Modularization plan | — (plan) | DONE (doc 03) — awaiting sign-off |
| 8 | Shared-cursor plan | — (plan) | DONE (doc 03) — awaiting sign-off |
| 9 | Response-system redesign plan | — (plan) | DONE (doc 04) — awaiting sign-off |

## What's left for YOU (none urgent, all in docs/refactor-notes/)
- **Rotate the SSH password** (doc 01) — only real security to-do. Discord
  token is NOT leaked (verified).
- **Music**: tell me which cause the logs confirm (doc 02), I'll patch the cog.
- **Pick** which of the planned refactors to start (docs 03, 04). I left the
  high-risk ones (cursor, big response refactor) un-started on purpose.

## Everything I changed is on this branch, one commit each
`git log main..refactor/hardening-2026-05-31 --oneline` shows the full set.
Nothing merged to main. Revert any single commit if it misbehaves.

## Notes / things for you to decide
- Server reachable only via LAN IP (192.168.1.101), not the Tailscale
  100.x in deploy.py — confirms deploy.py's HOST is stale (already noted in
  docs/08).
- Old sonarr.service journal still on the box (7546 lines) — useful for future
  tuning. I saved a local copy (gitignored).
