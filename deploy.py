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

# ============ CONFIG ============
HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22
REMOTE_DIR = '/root/sonarr/'
LOCAL_DIR = os.path.dirname(os.path.abspath(__file__))

# Files/dirs to EXCLUDE from upload (not needed on server)
EXCLUDE = {
    '.git', '.venv', 'venv', '__pycache__', '.env.example',
    'deploy.py', '.gitignore', '.vscode', '.idea',
    '*.pyc', '*.pyo', '*.egg-info', '.DS_Store', 'Thumbs.db',
    'data',  # data is preserved separately
}

# Data files to PRESERVE on the remote server
PRESERVE_DATA = [
    'data/training_data.json',     # ML training data (will retrain with new names)
    'bot_data.db',                 # User data, levels, relationships
    'playlists.json',              # Saved playlists
    'ai_memory.json',              # AI memories
    'server_config.json',          # Server configs
    'levels.json',                 # Level data
]

# Data files to DELETE on the remote server (stale)
DELETE_DATA = [
    'data/sklearn_model.pkl',      # Stale — trained on old category names
    'data/nb_model.json',          # Obsolete old model
]


def create_ssh_client(server, port, user, password):
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(server, port, user, password)
    return client


def exec_remote(ssh, cmd, label=""):
    """Execute a remote command and print output."""
    if label:
        print(f"  [{label}] {cmd}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(f"  {out}")
    if err and exit_code != 0:
        print(f"  ERROR: {err}")
    return exit_code, out, err


def should_exclude(path):
    """Check if a path should be excluded from upload."""
    parts = path.replace('\\', '/').split('/')
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
    """Collect all files to upload, respecting exclusions."""
    files = []
    for root, dirs, filenames in os.walk(local_dir):
        # Filter out excluded directories in-place
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
    print("SONARR DEPLOY — Full Nuke & Redeploy")
    print("=" * 60)
    
    # Collect local files
    files = collect_files(LOCAL_DIR)
    print(f"\nLocal files to upload: {len(files)}")
    
    try:
        print(f"\nConnecting to {HOST}:{PORT}...")
        ssh = create_ssh_client(HOST, PORT, USER, PASSWORD)
        print("Connected!\n")
        
        # ===== STEP 1: Backup data on remote =====
        print("=" * 40)
        print("STEP 1: Backup data on remote")
        print("=" * 40)
        exec_remote(ssh, f'mkdir -p /tmp/sonarr_backup', "mkdir")
        for data_file in PRESERVE_DATA:
            remote_path = f'{REMOTE_DIR}{data_file}'
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            exec_remote(ssh, f'cp -f {remote_path} {backup_path} 2>/dev/null || true', f"backup {data_file}")
        
        # ===== STEP 2: Stop service =====
        print("\n" + "=" * 40)
        print("STEP 2: Stop service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl stop sonarr.service', "stop")
        
        # ===== STEP 3: Nuke remote code =====
        print("\n" + "=" * 40)
        print("STEP 3: Nuke remote code directory")
        print("=" * 40)
        exec_remote(ssh, f'rm -rf {REMOTE_DIR}*', "nuke")
        exec_remote(ssh, f'rm -rf {REMOTE_DIR}.*', "nuke hidden")
        print("  Remote directory wiped clean.")
        
        # ===== STEP 4: Create directory structure =====
        print("\n" + "=" * 40)
        print("STEP 4: Create directory structure")
        print("=" * 40)
        
        # Collect unique directories
        dirs = set()
        for f in files:
            d = os.path.dirname(f)
            while d:
                dirs.add(d)
                d = os.path.dirname(d)
        
        for d in sorted(dirs):
            remote_path = f'{REMOTE_DIR}{d}'
            exec_remote(ssh, f'mkdir -p {remote_path}', "mkdir")
        exec_remote(ssh, f'mkdir -p {REMOTE_DIR}data', "mkdir data")
        
        # ===== STEP 5: Upload all files =====
        print("\n" + "=" * 40)
        print(f"STEP 5: Upload {len(files)} files")
        print("=" * 40)
        
        with SCPClient(ssh.get_transport()) as scp:
            for i, rel_path in enumerate(files, 1):
                local_path = os.path.join(LOCAL_DIR, rel_path)
                remote_path = f'{REMOTE_DIR}{rel_path}'
                scp.put(local_path, remote_path=remote_path)
                if i % 10 == 0 or i == len(files):
                    print(f"  [{i}/{len(files)}] uploaded...")
        
        print(f"  All {len(files)} files uploaded!")
        
        # ===== STEP 6: Restore data =====
        print("\n" + "=" * 40)
        print("STEP 6: Restore preserved data")
        print("=" * 40)
        for data_file in PRESERVE_DATA:
            backup_path = f'/tmp/sonarr_backup/{os.path.basename(data_file)}'
            remote_path = f'{REMOTE_DIR}{data_file}'
            exec_remote(ssh, f'cp -f {backup_path} {remote_path} 2>/dev/null || true', f"restore {data_file}")
        
        # Delete stale data files
        for stale in DELETE_DATA:
            exec_remote(ssh, f'rm -f {REMOTE_DIR}{stale}', f"delete stale {stale}")
        
        # Clean up backup
        exec_remote(ssh, 'rm -rf /tmp/sonarr_backup', "cleanup backup")
        
        # ===== STEP 7: Free space & install deps =====
        print("\n" + "=" * 40)
        print("STEP 7: Install dependencies")
        print("=" * 40)
        exec_remote(ssh, 'rm -rf ~/.cache/pip && rm -rf /tmp/* && apt-get clean 2>/dev/null || true', "free space")
        
        # Install requirements
        install_cmd = (
            f'cd {REMOTE_DIR} && '
            'if [ -d "venv" ]; then '
            '  venv/bin/pip install --no-cache-dir -r requirements.txt; '
            'else '
            '  pip3 install --break-system-packages --no-cache-dir -r requirements.txt; '
            'fi'
        )
        print("  Installing Python packages (this may take a while)...")
        exit_code, out, err = exec_remote(ssh, install_cmd, "pip install")
        
        # ===== STEP 8: Restart service =====
        print("\n" + "=" * 40)
        print("STEP 8: Restart service")
        print("=" * 40)
        exec_remote(ssh, 'systemctl daemon-reload', "daemon-reload")
        exit_code, _, _ = exec_remote(ssh, 'systemctl start sonarr.service', "start")
        
        if exit_code == 0:
            print("\n✅ Service started successfully!")
        else:
            print("\n❌ Failed to start service")
            exec_remote(ssh, 'journalctl -u sonarr.service -n 30 --no-pager', "logs")
        
        # ===== STEP 9: Verify =====
        print("\n" + "=" * 40)
        print("STEP 9: Verify")
        print("=" * 40)
        import time
        time.sleep(3)
        exec_remote(ssh, 'systemctl is-active sonarr.service', "status")
        exec_remote(ssh, 'journalctl -u sonarr.service -n 10 --no-pager', "recent logs")
        
        print("\n" + "=" * 60)
        print("DEPLOY COMPLETE!")
        print("=" * 60)
        
    except Exception as e:
        print(f"\n❌ Error: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()


if __name__ == '__main__':
    main()
