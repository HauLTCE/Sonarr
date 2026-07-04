"""
Full nuke-and-redeploy script for SONARR bot.

Strategy (fail-fast — any critical step that fails aborts the deploy):
1. Confirm target, connect (with timeouts + host-key policy).
2. Stop the service, checkpoint each preserved DB's WAL.
3. Back up all preserved data to /tmp AND VERIFY it — abort before the nuke if
   any file that exists on the server failed to back up (closes the data-loss path).
4. Nuke the remote code directory (keep venv).
5. Upload code fresh (raw .env excluded; a DEPLOY_*-stripped copy is uploaded
   instead so the root SSH password never lands on the server).
6. Restore preserved data (abort + keep the /tmp backup if restore fails).
7. Run DB migrations, install deps (real pip exit code, not tail's).
8. Restart the service and only report success if it is actually active.

Deferred (bigger, separate decisions — see the review): tarball/rsync upload
for speed+atomicity, moving runtime data out of the code dir to drop the
backup/restore dance entirely, and a release-dir + symlink swap for rollback.

Usage: python deploy.py [--yes]   (--yes / -y skips the confirmation prompt)
"""
import os
import sys
import time
import tempfile

import paramiko
from scp import SCPClient

try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    # python-dotenv isn't installed in this environment. Fall back to a tiny
    # built-in parser so deploy still works. Reads KEY=VALUE lines from a local
    # .env (ignoring comments/blank lines) into os.environ without overwriting
    # variables already set in the real environment.
    def load_dotenv(path=".env"):
        env_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), path)
        if not os.path.exists(env_path):
            return
        with open(env_path, "r", encoding="utf-8") as fh:
            for line in fh:
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                key, _, value = line.partition("=")
                key = key.strip()
                value = value.strip().strip('"').strip("'")
                os.environ.setdefault(key, value)

    load_dotenv()


class DeployError(Exception):
    """Raised when a critical deploy step fails so the whole run aborts."""


# ============ CONFIG (from environment — see .env.example) ============
# Secrets are NEVER hardcoded. Set DEPLOY_HOST / DEPLOY_USER / DEPLOY_PASSWORD
# (and optionally DEPLOY_PORT / DEPLOY_REMOTE_DIR) in your local .env.
HOST = os.getenv('DEPLOY_HOST')
USER = os.getenv('DEPLOY_USER', 'root')
PASSWORD = os.getenv('DEPLOY_PASSWORD')
PORT = int(os.getenv('DEPLOY_PORT', '22'))
REMOTE_DIR = os.getenv('DEPLOY_REMOTE_DIR', '/root/sonarr/')
LOCAL_DIR = os.path.dirname(os.path.abspath(__file__))

# Env keys stripped from the copy of .env uploaded to the server. deploy.py
# itself is never uploaded, so these are useless on the box — and DEPLOY_PASSWORD
# is the root SSH password, which must not be sitting in /root/sonarr/.env.
STRIP_ENV_PREFIXES = ('DEPLOY_',)

if not HOST or not PASSWORD:
    sys.exit("ERROR: DEPLOY_HOST and DEPLOY_PASSWORD must be set in .env")


def _validate_remote_dir(path):
    """Fail fast on a REMOTE_DIR that would make the nuke step catastrophic.

    REMOTE_DIR is interpolated raw into `find ... -exec rm -rf` and many other
    shell commands, so an empty / root / metacharacter-laced value could wipe
    the wrong thing or inject commands. Require an absolute, sufficiently nested
    path with no shell-significant characters.
    """
    if not path or not path.startswith('/'):
        sys.exit(f"ERROR: DEPLOY_REMOTE_DIR must be an absolute path (got: {path!r}).")

    normalized = path.rstrip('/')
    if normalized in ('', '/'):
        sys.exit("ERROR: refusing to deploy to filesystem root '/'.")

    segments = [seg for seg in normalized.split('/') if seg]
    if len(segments) < 2:
        sys.exit(
            f"ERROR: DEPLOY_REMOTE_DIR {path!r} is too shallow to nuke safely; "
            "expected something nested like /root/sonarr."
        )

    unsafe = set(' \t\n;&|`$()<>"\'*?!{}[]\\')
    if any(ch in unsafe for ch in path):
        sys.exit(f"ERROR: DEPLOY_REMOTE_DIR {path!r} contains unsafe shell characters.")

    # Normalize to a guaranteed single trailing slash (rest of the script assumes it).
    return normalized + '/'


REMOTE_DIR = _validate_remote_dir(REMOTE_DIR)

# Files/dirs to EXCLUDE from upload (not needed on server).
# NOTE: the raw '.env' is excluded here on purpose — a DEPLOY_*-stripped copy is
# uploaded separately (see _upload_filtered_env) so deploy secrets never ship.
EXCLUDE = {
    '.git', '.venv', 'venv', '__pycache__', '.env', '.env.example',
    'deploy.py', '.gitignore', '.vscode', '.idea',
    '*.pyc', '*.pyo', '*.egg-info', '.DS_Store', 'Thumbs.db',
    'data',  # data is preserved separately
    'scripts',  # training/eval scripts not needed on server
    'docs',  # development documentation
    'UPGRADE_PLAN.md',  # dev documentation
    # Database files — NEVER upload local dev databases
    'bot_data.db', 'bot_data.db-shm', 'bot_data.db-wal',
    'sonarr_memory.db', 'sonarr_memory.db-shm', 'sonarr_memory.db-wal',
    'check_server.py', 'test_imports.py',  # temp dev scripts
}

# Data files to PRESERVE on the remote server (backed up before wipe, restored after)
# The -wal / -shm sidecars are preserved too: if the WAL checkpoint below fails
# for any reason, the uncommitted transactions still live in the -wal, and
# backing up the consistent (db, -wal, -shm) set — captured while the service is
# stopped — keeps them. (After a successful checkpoint the -wal is empty, so
# restoring it is harmless.)
PRESERVE_DATA = [
    'data/training_data.json',
    'data/context.db',
    'data/context.db-wal',
    'data/context.db-shm',
    'data/elaine.db',       # E.L.A.I.N.E per-user affect/dialogue state (chat personality)
    'data/elaine.db-wal',
    'data/elaine.db-shm',
    'bot_data.db',          # levels, channel config, economy, adventure, titles — ALL user data
    'bot_data.db-wal',
    'bot_data.db-shm',
    'sonarr_memory.db',
    'sonarr_memory.db-wal',
    'sonarr_memory.db-shm',
    'playlists.json',
    'ai_memory.json',
]

# Backups are keyed by basename in a flat /tmp dir, so two preserved paths that
# share a basename would clobber each other. Catch that at startup, not at 2am.
_basenames = [os.path.basename(f) for f in PRESERVE_DATA]
_dupe_basenames = sorted({b for b in _basenames if _basenames.count(b) > 1})
if _dupe_basenames:
    sys.exit(
        "ERROR: PRESERVE_DATA has colliding basenames (flat backup would clobber): "
        + ", ".join(_dupe_basenames)
    )

# Data directories to PRESERVE on the remote server (backed up as tarballs)
PRESERVE_DIRS = []

# Data files to DELETE on the remote server (stale ML artifacts from the old
# classifier — the bot is now pure-deterministic and loads no models)
DELETE_DATA = [
    'data/sklearn_model.pkl',
    'data/nb_model.json',
]
DELETE_DIRS = [
    'data/chroma_db',
    'data/setfit_model',
    'data/spacy_textcat_model',
]


def create_ssh_client(server, port, user, password):
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    try:
        client.load_host_keys(os.path.expanduser('~/.ssh/known_hosts'))
    except (OSError, IOError):
        pass

    # Host-key verification. Default is WarningPolicy (connects but warns on an
    # unknown key) — strictly better than the old AutoAddPolicy, which trusted
    # any key silently (MITM-exposed while sending a root password). Set
    # DEPLOY_STRICT_HOST_KEY=true to reject unknown hosts outright.
    strict = os.getenv('DEPLOY_STRICT_HOST_KEY', 'false').lower() in ('1', 'true', 'yes')
    if strict:
        client.set_missing_host_key_policy(paramiko.RejectPolicy())
    else:
        client.set_missing_host_key_policy(paramiko.WarningPolicy())
        print("  [warn] host key not strictly verified "
              "(set DEPLOY_STRICT_HOST_KEY=true to enforce)")

    client.connect(server, port, user, password,
                   timeout=15, banner_timeout=30, auth_timeout=30)
    return client


def exec_remote(ssh, cmd, label="", check=False, timeout=None):
    """Run a remote command.

    Reads stdout/stderr BEFORE fetching the exit status: waiting on
    recv_exit_status() while the remote blocks on a full output buffer is a
    classic paramiko deadlock. With check=True a non-zero exit raises
    DeployError so the deploy aborts instead of silently continuing.
    """
    if label:
        print(f"  [{label}] {cmd[:80]}")
    _stdin, stdout, stderr = ssh.exec_command(cmd, timeout=timeout)
    out = stdout.read().decode(errors="replace").strip()
    err = stderr.read().decode(errors="replace").strip()
    exit_code = stdout.channel.recv_exit_status()
    if out:
        print(f"  {out[-300:]}" if len(out) > 300 else f"  {out}")
    if err and exit_code != 0:
        print(f"  ERROR: {err[-200:]}")
    if check and exit_code != 0:
        raise DeployError(f"remote step failed ({label or cmd[:60]}): exit {exit_code}"
                          + (f" — {err[-200:]}" if err else ""))
    return exit_code, out, err


def remote_exists(ssh, remote_path):
    """True if the given path exists on the remote (a regular file)."""
    return exec_remote(ssh, f'test -f "{remote_path}"')[0] == 0


def should_exclude(path):
    parts = path.replace('\\', '/').split('/')
    basename = parts[-1] if parts else ''
    # Check basename against EXCLUDE
    if basename in EXCLUDE:
        return True
    # Check .db files explicitly
    if basename.endswith('.db') or basename.endswith('.db-shm') or basename.endswith('.db-wal'):
        return True
    for part in parts:
        if part in EXCLUDE:
            return True
        if part.startswith('.') and part not in ('.env',):
            return True
        if part == '__pycache__':
            return True
        if part.endswith('.pyc') or part.endswith('.pyo'):
            return True
    return False


def collect_files(local_dir):
    files = []
    for root, dirs, filenames in os.walk(local_dir):
        dirs[:] = [d for d in dirs if d not in EXCLUDE and not d.startswith('.') or d == '.env']
        for f in filenames:
            if f in EXCLUDE or f.endswith(('.pyc', '.pyo')):
                continue
            full_path = os.path.join(root, f)
            rel_path = os.path.relpath(full_path, local_dir).replace('\\', '/')
            if not should_exclude(rel_path):
                files.append(rel_path)
    return sorted(files)


def build_filtered_env(local_env_path):
    """Return .env text with deploy-only keys (STRIP_ENV_PREFIXES) removed.

    Returns None if there's no local .env. Comments and blank lines are kept so
    the uploaded file stays readable; only KEY=VALUE lines whose key matches a
    stripped prefix are dropped.
    """
    if not os.path.exists(local_env_path):
        return None
    kept, dropped = [], []
    with open(local_env_path, "r", encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            line = raw.rstrip("\n")
            stripped = line.strip()
            if stripped and not stripped.startswith("#") and "=" in stripped:
                key = stripped.split("=", 1)[0].strip()
                if any(key.startswith(p) for p in STRIP_ENV_PREFIXES):
                    dropped.append(key)
                    continue
            kept.append(line)
    if dropped:
        print(f"  Stripped deploy-only keys from uploaded .env: {', '.join(dropped)}")
    return "\n".join(kept).rstrip("\n") + "\n"


def upload_filtered_env(scp):
    """Upload a DEPLOY_*-stripped copy of the local .env as REMOTE_DIR/.env."""
    content = build_filtered_env(os.path.join(LOCAL_DIR, ".env"))
    if content is None:
        print("  No local .env found; skipping .env upload (add one on the server).")
        return
    fd, tmp_path = tempfile.mkstemp(suffix=".env")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as tmp:
            tmp.write(content)
        scp.put(tmp_path, remote_path=f"{REMOTE_DIR}.env")
        print("  Uploaded filtered .env")
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass


def confirm_target(auto_yes):
    print(f"\nTarget: {USER}@{HOST}:{PORT}  dir={REMOTE_DIR}")
    if auto_yes:
        return True
    if not sys.stdin.isatty():
        print("Non-interactive and --yes not given; aborting for safety.")
        return False
    reply = input("This will STOP the service and REPLACE all remote code. "
                  "Type 'deploy' to continue: ").strip()
    return reply == "deploy"


def main():
    print("=" * 60)
    print("SONARR DEPLOY - Full Nuke & Redeploy")
    print("=" * 60)

    auto_yes = any(a in ("-y", "--yes") for a in sys.argv[1:])
    if not confirm_target(auto_yes):
        print("Aborted.")
        sys.exit(1)

    files = collect_files(LOCAL_DIR)
    print(f"\nLocal files to upload: {len(files)}")

    ssh = None
    try:
        print(f"\nConnecting to {HOST}:{PORT}...")
        ssh = create_ssh_client(HOST, PORT, USER, PASSWORD)
        print("Connected!\n")

        # STEP 1: Stop service FIRST (flushes WAL, ensures clean DB state).
        # Best-effort: the unit may not exist yet on a first deploy.
        print("=" * 40)
        print("STEP 1: Stop service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl stop sonarr.service', "stop")
        time.sleep(2)  # give it a moment to release file handles

        # STEP 2: Checkpoint WAL, back up data, and VERIFY before we nuke anything.
        print("\n" + "=" * 40)
        print("STEP 2: Backup data on remote (service stopped, WAL flushed)")
        print("=" * 40)
        exec_remote(ssh, 'mkdir -p /tmp/sonarr_backup', "mkdir", check=True)

        # Flush each preserved DB's WAL into its main file before copying.
        # The -c payload is wrapped in SINGLE quotes at the shell level so the
        # double quotes it contains stay literal (a previous version used \"
        # escapes inside a double-quoted string, which the shell collapsed into
        # a syntax error that 2>/dev/null then hid — so the WAL was never
        # actually checkpointed). stderr stays visible so real failures surface.
        for db_file in [f for f in PRESERVE_DATA if f.endswith('.db')]:
            py_payload = (
                'import sqlite3; '
                f'c=sqlite3.connect("{db_file}"); '
                'c.execute("PRAGMA wal_checkpoint(TRUNCATE)"); '
                'c.close(); print("WAL flushed")'
            )
            flush_cmd = (
                f'cd {REMOTE_DIR} && '
                f'if [ -d "venv" ] && [ -f "{db_file}" ]; then '
                f"venv/bin/python3 -c '{py_payload}'; "
                f'fi'
            )
            exec_remote(ssh, flush_cmd, f"flush WAL {db_file}")

        # Back up only files that actually exist, and confirm each backup landed.
        backup_failures = []
        backed_up = 0
        for data_file in PRESERVE_DATA:
            remote_path = f'{REMOTE_DIR}{data_file}'
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            if not remote_exists(ssh, remote_path):
                continue  # nothing to preserve (e.g. first deploy, empty -wal)
            ec, _, _ = exec_remote(ssh, f'cp -f "{remote_path}" "{backup_path}"', f"backup {data_file}")
            # test -f (not -s): a checkpointed -wal is legitimately 0 bytes.
            if ec != 0 or not remote_exists(ssh, backup_path):
                backup_failures.append(data_file)
            else:
                backed_up += 1
        for data_dir in PRESERVE_DIRS:
            dir_name = data_dir.replace('/', '_')
            remote_path = f'{REMOTE_DIR}{data_dir}'
            backup_path = f'/tmp/sonarr_backup/{dir_name}.tar.gz'
            exec_remote(ssh, f'if [ -d "{remote_path}" ]; then tar czf {backup_path} -C {REMOTE_DIR} {data_dir}; fi', f"backup {data_dir}")

        if backup_failures:
            # Refuse to nuke — the /tmp copies are incomplete and the originals
            # are about to be deleted. This is the data-loss guard.
            raise DeployError(
                "Backup failed for existing files; refusing to nuke: "
                + ", ".join(backup_failures)
            )
        print(f"  [OK] Backed up {backed_up} existing data file(s) to /tmp/sonarr_backup")

        # STEP 3: Nuke remote code (but keep venv!)
        print("\n" + "=" * 40)
        print("STEP 3: Nuke remote code (keep venv)")
        print("=" * 40)
        # REMOTE_DIR is validated at startup; guard again so we never run the
        # recursive delete against a path that doesn't exist.
        nuke_cmd = (
            f'test -d "{REMOTE_DIR}" && '
            f'find {REMOTE_DIR} -maxdepth 1 -not -name "venv" -not -name "." '
            f'-not -path "{REMOTE_DIR}" -exec rm -rf {{}} +'
        )
        exec_remote(ssh, nuke_cmd, "nuke code", check=True)
        print("  Remote code wiped (venv preserved).")

        # STEP 4: Create directory structure
        print("\n" + "=" * 40)
        print("STEP 4: Create directory structure")
        print("=" * 40)
        dirs = set()
        for f in files:
            d = os.path.dirname(f)
            while d:
                dirs.add(d)
                d = os.path.dirname(d)
        for d in sorted(dirs):
            exec_remote(ssh, f'mkdir -p {REMOTE_DIR}{d}', "mkdir", check=True)
        exec_remote(ssh, f'mkdir -p {REMOTE_DIR}data', "mkdir data", check=True)

        # STEP 5: Upload all files (raw .env excluded; filtered copy sent after).
        print("\n" + "=" * 40)
        print(f"STEP 5: Upload {len(files)} files")
        print("=" * 40)
        with SCPClient(ssh.get_transport()) as scp:
            for i, rel_path in enumerate(files, 1):
                local_path = os.path.join(LOCAL_DIR, rel_path)
                remote_path = f'{REMOTE_DIR}{rel_path}'
                try:
                    scp.put(local_path, remote_path=remote_path)
                except Exception as exc:
                    raise DeployError(f"upload failed for {rel_path}: {exc}")
                if i % 10 == 0 or i == len(files):
                    print(f"  [{i}/{len(files)}] uploaded...")
            upload_filtered_env(scp)
        print(f"  All {len(files)} files uploaded!")

        # STEP 6: Restore data
        print("\n" + "=" * 40)
        print("STEP 6: Restore preserved data")
        print("=" * 40)
        restore_failures = []
        for data_file in PRESERVE_DATA:
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            remote_path = f'{REMOTE_DIR}{data_file}'
            if not remote_exists(ssh, backup_path):
                continue  # nothing was backed up for this one
            ec, _, _ = exec_remote(ssh, f'cp -f "{backup_path}" "{remote_path}"', f"restore {data_file}")
            if ec != 0 or not remote_exists(ssh, remote_path):
                restore_failures.append(data_file)
        for data_dir in PRESERVE_DIRS:
            dir_name = data_dir.replace('/', '_')
            backup_path = f'/tmp/sonarr_backup/{dir_name}.tar.gz'
            exec_remote(ssh, f'if [ -f "{backup_path}" ]; then tar xzf {backup_path} -C {REMOTE_DIR}; fi', f"restore {data_dir}")
        for stale in DELETE_DATA:
            exec_remote(ssh, f'rm -f {REMOTE_DIR}{stale}', f"delete stale {stale}")
        for stale_dir in DELETE_DIRS:
            exec_remote(ssh, f'rm -rf {REMOTE_DIR}{stale_dir}', f"delete stale {stale_dir}")

        if restore_failures:
            # Leave /tmp/sonarr_backup in place so the data can be recovered by hand.
            raise DeployError(
                "Restore failed for: " + ", ".join(restore_failures)
                + " — backup left at /tmp/sonarr_backup for manual recovery."
            )

        # Verify restored DB
        _, out, _ = exec_remote(ssh, f'stat --format="%s" {REMOTE_DIR}bot_data.db 2>/dev/null || echo "0"', "verify restored DB")
        restored_size = int(out.strip()) if out.strip().isdigit() else 0
        if restored_size > 0:
            print(f"  [OK] Restored bot_data.db: {restored_size:,} bytes")
        else:
            print(f"  [WARN] bot_data.db not found after restore -- will be created fresh on startup")

        exec_remote(ssh, 'rm -rf /tmp/sonarr_backup', "cleanup backup")

        # STEP 6.5: Trigger DB migrations
        print("\n" + "=" * 40)
        print("STEP 6.5: Run DB migrations")
        print("=" * 40)
        migrate_script = 'import sys; sys.path.insert(0,"."); from utils.database import db; print("DB migrations complete")'
        exec_remote(ssh, f'cd {REMOTE_DIR} && venv/bin/python3 -c \'{migrate_script}\'', "migrate", check=True)

        # STEP 7: Install deps
        print("\n" + "=" * 40)
        print("STEP 7: Install dependencies")
        print("=" * 40)
        # Redirect to a log and check pip's OWN exit code. The old version piped
        # pip through `tail`, so the pipeline's exit status was tail's (always 0)
        # and a failed install looked successful.
        install_cmd = (
            f'cd {REMOTE_DIR} && '
            '{ [ -d venv ] || python3 -m venv venv; } && '
            'venv/bin/pip install --no-cache-dir -r requirements.txt > /tmp/sonarr_pip.log 2>&1'
        )
        print("  Installing packages...")
        ec, _, _ = exec_remote(ssh, install_cmd, "pip install")
        exec_remote(ssh, 'tail -n 15 /tmp/sonarr_pip.log', "pip output")
        if ec != 0:
            raise DeployError("pip install failed (see /tmp/sonarr_pip.log on the server)")

        # STEP 8: Restart service
        print("\n" + "=" * 40)
        print("STEP 8: Restart service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl daemon-reload', "daemon-reload")
        exec_remote(ssh, 'systemctl enable sonarr.service', "enable on boot")
        exec_remote(ssh, 'systemctl start sonarr.service', "start")

        # STEP 9: Verify the service is actually running before declaring success.
        time.sleep(3)
        print("\n" + "=" * 40)
        print("STEP 9: Verify")
        print("=" * 40)
        _, active, _ = exec_remote(ssh, 'systemctl is-active sonarr.service', "status")
        if active.strip() != "active":
            exec_remote(ssh, 'journalctl -u sonarr.service -n 30 --no-pager', "logs")
            raise DeployError(f"service is not active after start (state: {active.strip() or 'unknown'})")

        print("\n" + "=" * 60)
        print("DEPLOY COMPLETE!")
        print("=" * 60)

    except DeployError as e:
        print(f"\nDEPLOY FAILED: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"\nUnexpected error: {e}")
        sys.exit(1)
    finally:
        if ssh is not None:
            ssh.close()


if __name__ == '__main__':
    main()
