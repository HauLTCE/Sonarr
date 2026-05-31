# Secrets — what I fixed, and what still needs YOU

## What I already did (committed on the branch)
- `boost_player.py` (tracked) and `deploy.py` (gitignored) no longer hardcode
  the root SSH password. They read `DEPLOY_*` from `.env` via `python-dotenv`.
- Added `.env.example` so the required vars are documented without real values.
- Put the real deploy values into your local `.env` (gitignored), so the
  scripts keep working with zero behavior change.

## What I CANNOT safely do alone (needs a human)

The working tree is clean now, but secrets are still in **git history**.

### I checked the history for you (2026-05-31):
- **`.env` was NEVER committed** (`git log --all -- .env` is empty). Your
  **Discord token is NOT in git history.** Good. No urgent token action.
- **The SSH password `12345` IS in history** — it appears in many old commits
  via `boost_player.py`. This is the one real leak.

### Step 1 — ROTATE the SSH password (the only real leak)
A leaked secret in git history is leaked forever, even after a scrub (someone
may have cloned). Rotation is what actually protects you:
- **SSH password:** `passwd root` on the server (192.168.1.101), update
  `DEPLOY_PASSWORD` in `.env`. Better: switch to an SSH **key** and disable
  root password login entirely. I can write that change for you if you want.
- **Discord token:** NOT in history (verified), so no rotation needed. Only
  reset it if you ever pasted it somewhere public by accident.

### Step 2 — SCRUB history (optional, cosmetic after rotation)
Only worth it if the repo is/will be public. This rewrites history and needs a
force-push, which I won't do unsupervised:
```
# using git-filter-repo (recommended over filter-branch)
pip install git-filter-repo
git filter-repo --replace-text <(echo "12345==>***REMOVED***")
# then re-add the remote and force-push (coordinate with anyone who cloned)
git remote add origin https://github.com/HauLTCE/SONARR.git
git push --force --all
```
**Risks:** rewrites every commit SHA, breaks open PRs/branches, anyone with a
clone must re-clone. The backup branch `origin/backup/huy-pre-reset-...`
suggests you've done a history reset before, so you know the drill.

## My recommendation
Rotate both secrets today (5 minutes, fully protects you). Treat the history
scrub as low-priority unless the repo goes public. Don't force-push while sick
and unsupervised — that's exactly when force-push accidents happen.
