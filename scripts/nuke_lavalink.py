"""
Nuke Lavalink and reinstall from scratch.
Stops Lavalink, deletes everything, re-downloads Lavalink.jar,
writes fresh application.yml and service file, starts it up.
Then restarts the bot.
"""
import paramiko
import sys
import time

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22

LAVALINK_URL = 'https://github.com/lavalink-devs/Lavalink/releases/download/4.0.8/Lavalink.jar'
LAVALINK_DIR = '/root/lavalink'

APPLICATION_YML = """server:
  port: 2333
  address: 0.0.0.0
lavalink:
  plugins:
    - dependency: "dev.lavalink.youtube:youtube-plugin:1.18.1"
      snapshot: false
  server:
    password: "youshallnotpass"
    sources:
      youtube: false
      bandcamp: true
      soundcloud: true
      twitch: true
      vimeo: true
      http: true
      local: false
    bufferDurationMs: 400
    frameBufferDurationMs: 5000
    opusEncodingQuality: 10
    resamplingQuality: LOW
    trackStuckThresholdMs: 10000
    useSeekGhosting: true
    youtubePlaylistLoadLimit: 6
    playerUpdateInterval: 5
    youtubeSearchEnabled: true
    soundcloudSearchEnabled: true
    gc-warnings: true

plugins:
  youtube:
    enabled: true
    allowSearch: true
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - MUSIC
      - WEB
      - ANDROID_VR
      - TVHTML5_SIMPLY
      - TV

logging:
  level:
    lavalink.server: INFO
    dev.lavalink.youtube: INFO
"""

SERVICE_CONTENT = f"""[Unit]
Description=Lavalink Node
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory={LAVALINK_DIR}
ExecStart=/usr/bin/java -jar {LAVALINK_DIR}/Lavalink.jar
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
"""

def create_ssh_client():
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, PORT, USER, PASSWORD)
    return client

def exec_remote(ssh, cmd, label=""):
    tag = f"[{label}] " if label else ""
    print(f"  {tag}{cmd[:120]}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode('utf-8', 'ignore').strip()
    err = stderr.read().decode('utf-8', 'ignore').strip()
    if out:
        lines = out.split('\n')
        for line in lines[-10:]:
            print(f"    {line}")
    if err and exit_code != 0:
        print(f"    ERR: {err[-300:]}")
    return exit_code, out, err


def main():
    print("=" * 60)
    print("NUKE & REINSTALL LAVALINK")
    print("=" * 60)
    
    try:
        print(f"\nConnecting to {HOST}...")
        ssh = create_ssh_client()
        print("Connected!\n")

        # Step 1: Stop everything
        print("=== Step 1: Stop Lavalink & Bot ===")
        exec_remote(ssh, "systemctl stop lavalink.service 2>/dev/null || true", "stop lavalink")
        exec_remote(ssh, "systemctl stop sonarr.service 2>/dev/null || true", "stop bot")
        # Kill any lingering java processes
        exec_remote(ssh, "pkill -f Lavalink.jar 2>/dev/null || true", "kill java")
        time.sleep(2)

        # Step 2: Nuke Lavalink completely
        print("\n=== Step 2: Nuke Lavalink directory ===")
        exec_remote(ssh, f"rm -rf {LAVALINK_DIR}", "nuke dir")
        exec_remote(ssh, "rm -f /etc/systemd/system/lavalink.service", "nuke service")
        exec_remote(ssh, "systemctl daemon-reload", "reload systemd")
        print("  Lavalink completely nuked.")

        # Step 3: Fresh install
        print("\n=== Step 3: Create fresh Lavalink directory ===")
        exec_remote(ssh, f"mkdir -p {LAVALINK_DIR}", "mkdir")

        print("\n=== Step 4: Download Lavalink.jar ===")
        exec_remote(ssh, f"curl -L {LAVALINK_URL} -o {LAVALINK_DIR}/Lavalink.jar", "download")

        print("\n=== Step 5: Write application.yml ===")
        sftp = ssh.open_sftp()
        with sftp.file(f"{LAVALINK_DIR}/application.yml", 'w') as f:
            f.write(APPLICATION_YML)
        print("  application.yml written.")

        print("\n=== Step 6: Write systemd service ===")
        with sftp.file("/etc/systemd/system/lavalink.service", 'w') as f:
            f.write(SERVICE_CONTENT)
        sftp.close()
        print("  lavalink.service written.")

        # Step 7: Start Lavalink
        print("\n=== Step 7: Start Lavalink ===")
        exec_remote(ssh, "systemctl daemon-reload", "reload")
        exec_remote(ssh, "systemctl enable lavalink.service", "enable")
        exec_remote(ssh, "systemctl start lavalink.service", "start")
        print("  Waiting 15s for Lavalink to download youtube plugin and start...")
        time.sleep(15)
        
        code, _, _ = exec_remote(ssh, "systemctl is-active lavalink.service", "status")
        if code != 0:
            print("  FAILED — checking logs:")
            exec_remote(ssh, "journalctl -u lavalink.service -n 30 --no-pager", "logs")
            sys.exit(1)
        exec_remote(ssh, "journalctl -u lavalink.service -n 10 --no-pager", "logs")

        # Step 8: Restart bot
        print("\n=== Step 8: Restart bot ===")
        exec_remote(ssh, "systemctl start sonarr.service", "start bot")
        print("  Waiting 15s for bot to start...")
        time.sleep(15)
        exec_remote(ssh, "systemctl is-active sonarr.service", "bot status")
        exec_remote(ssh, "journalctl -u sonarr.service -n 10 --no-pager", "bot logs")

        print("\n" + "=" * 60)
        print("NUKE & REINSTALL COMPLETE!")
        print("=" * 60)

    except Exception as e:
        print(f"\nError: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()

if __name__ == '__main__':
    main()
