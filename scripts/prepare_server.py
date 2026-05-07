"""
Final deployment: Nuke Lavalink, install FFmpeg, update yt-dlp, deploy new bot code.
"""
import paramiko
import sys
import time

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22

def exec_remote(ssh, cmd, label=""):
    tag = f"[{label}] " if label else ""
    print(f"  {tag}{cmd[:140]}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode('utf-8', 'ignore').strip()
    err = stderr.read().decode('utf-8', 'ignore').strip()
    if out:
        for line in out.split('\n')[-6:]:
            print(f"    {line}")
    if err and exit_code != 0:
        print(f"    ERR: {err[-300:]}")
    return exit_code, out, err


def main():
    print("=" * 60)
    print("NUKE LAVALINK + PREPARE SERVER")
    print("=" * 60)

    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(HOST, PORT, USER, PASSWORD)
    print(f"Connected to {HOST}\n")

    # 1. Stop services
    print("=== Step 1: Stop everything ===")
    exec_remote(ssh, "systemctl stop sonarr.service 2>/dev/null || true", "stop bot")
    exec_remote(ssh, "systemctl stop lavalink.service 2>/dev/null || true", "stop lavalink")
    exec_remote(ssh, "pkill -f Lavalink.jar 2>/dev/null || true", "kill java")

    # 2. Nuke Lavalink completely
    print("\n=== Step 2: Nuke Lavalink ===")
    exec_remote(ssh, "systemctl disable lavalink.service 2>/dev/null || true", "disable")
    exec_remote(ssh, "rm -f /etc/systemd/system/lavalink.service", "rm service")
    exec_remote(ssh, "rm -rf /root/lavalink", "rm dir")
    exec_remote(ssh, "systemctl daemon-reload", "daemon-reload")

    # 3. Nuke yt-cipher Docker container
    print("\n=== Step 3: Remove yt-cipher container ===")
    exec_remote(ssh, "docker stop yt-cipher 2>/dev/null; docker rm yt-cipher 2>/dev/null || true", "rm container")

    # 4. Verify Lavalink is gone
    code, _, _ = exec_remote(ssh, "ls /root/lavalink 2>&1 || echo 'GONE'", "verify")
    code2, _, _ = exec_remote(ssh, "systemctl is-enabled lavalink.service 2>&1 || echo 'DISABLED'", "verify svc")

    # 5. Install FFmpeg
    print("\n=== Step 4: Ensure FFmpeg is installed ===")
    code, out, _ = exec_remote(ssh, "ffmpeg -version 2>/dev/null | head -1", "check ffmpeg")
    if code != 0 or 'ffmpeg' not in out.lower():
        print("  Installing FFmpeg...")
        exec_remote(ssh, "apt-get update -y && apt-get install -y ffmpeg", "install")
    else:
        print(f"  FFmpeg already installed: {out.split(chr(10))[0]}")

    # 6. Update yt-dlp on server
    print("\n=== Step 5: Update yt-dlp on server ===")
    exec_remote(ssh, "cd /root/sonarr && venv/bin/pip install -U yt-dlp 2>&1 | tail -3", "update yt-dlp")
    exec_remote(ssh, "cd /root/sonarr && venv/bin/python -c \"import yt_dlp; print('yt-dlp', yt_dlp.version.__version__)\"", "verify version")

    # 7. Remove wavelink from server venv
    print("\n=== Step 6: Remove wavelink from server ===")
    exec_remote(ssh, "cd /root/sonarr && venv/bin/pip uninstall -y wavelink 2>/dev/null || echo 'already gone'", "rm wavelink")

    print("\n" + "=" * 60)
    print("SERVER PREPARED! Now run: python deploy.py")
    print("=" * 60)

    ssh.close()

if __name__ == '__main__':
    main()
