"""
Full nuke-and-redeploy script for SONARR bot.

Strategy:
1. Backup data/ directory on remote (DB, training data, etc.)
2. Nuke the entire remote code directory
3. Upload all code files fresh
4. Restore data/ directory
5. Clean old ML model cache (stale category names)
6. Install/update requirements
7. Restart the service
"""
import paramiko
from scp import SCPClient
import os
import sys
from dotenv import load_dotenv

load_dotenv()

# ============ CONFIG (from environment — see .env.example) ============
# Secrets are NEVER hardcoded. Set DEPLOY_HOST / DEPLOY_USER / DEPLOY_PASSWORD
# (and optionally DEPLOY_PORT / DEPLOY_REMOTE_DIR) in your local .env.
HOST = os.getenv('DEPLOY_HOST')
USER = os.getenv('DEPLOY_USER', 'root')
PASSWORD = os.getenv('DEPLOY_PASSWORD')
PORT = int(os.getenv('DEPLOY_PORT', '22'))
REMOTE_DIR = os.getenv('DEPLOY_REMOTE_DIR', '/root/sonarr/')
LOCAL_DIR = os.path.dirname(os.path.abspath(__file__))

if not HOST or not PASSWORD:
    sys.exit("ERROR: DEPLOY_HOST and DEPLOY_PASSWORD must be set in .env")

# Files/dirs to EXCLUDE from upload (not needed on server)
EXCLUDE = {
    '.git', '.venv', 'venv', '__pycache__', '.env.example',
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
PRESERVE_DATA = [
    'data/training_data.json',
    'data/context.db',
    'bot_data.db',          # levels, channel config, economy, adventure, titles — ALL user data
    'sonarr_memory.db',
    'playlists.json',
    'ai_memory.json',
]

# Data directories to PRESERVE on the remote server (backed up as tarballs)
PRESERVE_DIRS = [
    'data/chroma_db',
    'data/setfit_model',
    'data/spacy_textcat_model',
]

# Data files to DELETE on the remote server (stale)
DELETE_DATA = [
    'data/sklearn_model.pkl',
    'data/nb_model.json',
]


def create_ssh_client(server, port, user, password):
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(server, port, user, password)
    return client


def exec_remote(ssh, cmd, label=""):
    if label:
        print(f"  [{label}] {cmd[:80]}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(f"  {out[-300:]}" if len(out) > 300 else f"  {out}")
    if err and exit_code != 0:
        print(f"  ERROR: {err[-200:]}")
    return exit_code, out, err


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


def main():
    print("=" * 60)
    print("SONARR DEPLOY - Full Nuke & Redeploy")
    print("=" * 60)

    files = collect_files(LOCAL_DIR)
    print(f"\nLocal files to upload: {len(files)}")

    try:
        print(f"\nConnecting to {HOST}:{PORT}...")
        ssh = create_ssh_client(HOST, PORT, USER, PASSWORD)
        print("Connected!\n")

        # STEP 1: Stop service FIRST (flushes WAL, ensures clean DB state)
        print("=" * 40)
        print("STEP 1: Stop service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl stop sonarr.service', "stop")
        import time
        time.sleep(2)  # Let it fully shut down

        # STEP 2: Flush WAL and backup data
        print("\n" + "=" * 40)
        print("STEP 2: Backup data on remote (service stopped, WAL flushed)")
        print("=" * 40)
        exec_remote(ssh, 'mkdir -p /tmp/sonarr_backup', "mkdir")

        # Flush WAL to main DB file before copying
        wal_flush_script = (
            f'cd {REMOTE_DIR} && '
            'if [ -d "venv" ] && [ -f "bot_data.db" ]; then '
            '  venv/bin/python3 -c "import sqlite3; c=sqlite3.connect(\"bot_data.db\"); '
            '  c.execute(\"PRAGMA wal_checkpoint(TRUNCATE)\"); c.close(); '
            '  print(\"WAL flushed\")" 2>/dev/null; '
            'fi'
        )
        exec_remote(ssh, wal_flush_script, "flush WAL")

        for data_file in PRESERVE_DATA:
            remote_path = f'{REMOTE_DIR}{data_file}'
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            exit_code, _, _ = exec_remote(ssh, f'cp -f {remote_path} {backup_path}', f"backup {data_file}")
            if exit_code != 0:
                print(f"  WARNING: Failed to backup {data_file} (may not exist yet)")
        for data_dir in PRESERVE_DIRS:
            dir_name = data_dir.replace('/', '_')
            remote_path = f'{REMOTE_DIR}{data_dir}'
            backup_path = f'/tmp/sonarr_backup/{dir_name}.tar.gz'
            exec_remote(ssh, f'if [ -d "{remote_path}" ]; then tar czf {backup_path} -C {REMOTE_DIR} {data_dir}; fi', f"backup {data_dir}")

        # Verify backup integrity
        print("\n  Verifying backup...")
        exit_code, out, _ = exec_remote(ssh, 'ls -la /tmp/sonarr_backup/', "verify backup")
        exit_code, out, _ = exec_remote(ssh, 'stat --format="%s" /tmp/sonarr_backup/bot_data.db 2>/dev/null || echo "0"', "check DB size")
        db_size = int(out.strip()) if out.strip().isdigit() else 0
        if db_size > 0:
            print(f"  [OK] bot_data.db backup: {db_size:,} bytes")
        else:
            print(f"  [WARN] No existing bot_data.db to backup (first deploy?)")

        # STEP 3: Nuke remote code (but keep venv!)
        print("\n" + "=" * 40)
        print("STEP 3: Nuke remote code (keep venv)")
        print("=" * 40)
        exec_remote(ssh, f'find {REMOTE_DIR} -maxdepth 1 -not -name "venv" -not -name "." -not -path "{REMOTE_DIR}" -exec rm -rf {{}} +', "nuke code")
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
            exec_remote(ssh, f'mkdir -p {REMOTE_DIR}{d}', "mkdir")
        exec_remote(ssh, f'mkdir -p {REMOTE_DIR}data', "mkdir data")

        # STEP 5: Upload all files (DB files excluded!)
        print("\n" + "=" * 40)
        print(f"STEP 5: Upload {len(files)} files (DB files excluded)")
        print("=" * 40)
        with SCPClient(ssh.get_transport()) as scp:
            for i, rel_path in enumerate(files, 1):
                local_path = os.path.join(LOCAL_DIR, rel_path)
                remote_path = f'{REMOTE_DIR}{rel_path}'
                scp.put(local_path, remote_path=remote_path)
                if i % 10 == 0 or i == len(files):
                    print(f"  [{i}/{len(files)}] uploaded...")
        print(f"  All {len(files)} files uploaded!")

        # STEP 6: Restore data
        print("\n" + "=" * 40)
        print("STEP 6: Restore preserved data")
        print("=" * 40)
        for data_file in PRESERVE_DATA:
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            remote_path = f'{REMOTE_DIR}{data_file}'
            exit_code, _, _ = exec_remote(ssh, f'if [ -f "{backup_path}" ]; then cp -f {backup_path} {remote_path} && echo "OK"; else echo "SKIP (no backup)"; fi', f"restore {data_file}")
        for data_dir in PRESERVE_DIRS:
            dir_name = data_dir.replace('/', '_')
            backup_path = f'/tmp/sonarr_backup/{dir_name}.tar.gz'
            exec_remote(ssh, f'if [ -f "{backup_path}" ]; then tar xzf {backup_path} -C {REMOTE_DIR}; fi', f"restore {data_dir}")
        for stale in DELETE_DATA:
            exec_remote(ssh, f'rm -f {REMOTE_DIR}{stale}', f"delete stale {stale}")

        # Verify restored DB
        exit_code, out, _ = exec_remote(ssh, f'stat --format="%s" {REMOTE_DIR}bot_data.db 2>/dev/null || echo "0"', "verify restored DB")
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
        exec_remote(ssh, f'cd {REMOTE_DIR} && venv/bin/python3 -c \'{migrate_script}\'', "migrate")

        # STEP 7: Install deps (skip if venv exists)
        print("\n" + "=" * 40)
        print("STEP 7: Install dependencies")
        print("=" * 40)
        install_cmd = (
            f'cd {REMOTE_DIR} && '
            'if [ -d "venv" ]; then '
            '  venv/bin/pip install --no-cache-dir -r requirements.txt 2>&1 | tail -5; '
            'else '
            '  python3 -m venv venv && '
            '  venv/bin/pip install --no-cache-dir torch --index-url https://download.pytorch.org/whl/cpu && '
            '  venv/bin/pip install --no-cache-dir -r requirements.txt; '
            'fi'
        )
        print("  Installing packages...")
        exec_remote(ssh, install_cmd, "pip install")

        # STEP 8: Restart service
        print("\n" + "=" * 40)
        print("STEP 8: Restart service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl daemon-reload', "daemon-reload")
        exit_code, _, _ = exec_remote(ssh, 'systemctl start sonarr.service', "start")

        if exit_code == 0:
            print("\n  Service started successfully!")
        else:
            print("\n  Failed to start service")
            exec_remote(ssh, 'journalctl -u sonarr.service -n 30 --no-pager', "logs")

        # STEP 9: Verify
        import time
        time.sleep(3)
        print("\n" + "=" * 40)
        print("STEP 9: Verify")
        print("=" * 40)
        exec_remote(ssh, 'systemctl is-active sonarr.service', "status")

        print("\n" + "=" * 60)
        print("DEPLOY COMPLETE!")
        print("=" * 60)

    except Exception as e:
        print(f"\nError: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()


if __name__ == '__main__':
    main()
