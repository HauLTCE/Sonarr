"""
Patch the remote Lavalink config to use the youtube-source plugin
(required by Lavalink 4.x — built-in YouTube was removed).
Also fixes the bot .env on the server to point to localhost since
Lavalink now runs on the same machine as the bot.
"""
import paramiko
import sys
import time

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22
LAVALINK_DIR = '/root/lavalink'
BOT_DIR = '/root/sonarr'

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
    gc-warnings: true

plugins:
  youtube:
    enabled: true
    allowSearch: true
    remoteCipher:
      url: "http://127.0.0.1:8001/"
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - MUSIC
      - TVHTML5EMBEDDED
      - WEB
      - ANDROID_VR

logging:
  level:
    lavalink.server: INFO
    dev.lavalink.youtube: INFO
"""

def create_ssh_client():
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, PORT, USER, PASSWORD)
    return client

def exec_remote(ssh, cmd, label=""):
    tag = f"[{label}] " if label else ""
    print(f"  {tag}{cmd[:100]}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode('utf-8', 'ignore').strip()
    err = stderr.read().decode('utf-8', 'ignore').strip()
    if out:
        print(f"    {out[-500:]}")
    if err and exit_code != 0:
        print(f"    ERR: {err[-200:]}")
    return exit_code, out, err

def main():
    print(f"Connecting to {HOST}...")
    try:
        ssh = create_ssh_client()
        print("Connected!\n")

        # 0. Start yt-cipher docker container
        print("=== Step 0: Start yt-cipher docker container ===")
        exec_remote(ssh, "docker pull ghcr.io/kikkia/yt-cipher:master", "docker-pull")
        exec_remote(ssh, "docker stop yt-cipher || true", "docker-stop")
        exec_remote(ssh, "docker rm yt-cipher || true", "docker-rm")
        exec_remote(ssh, "docker run -d --name yt-cipher -e OVERRIDE_PLAYER_VARIANT=IAS --restart always --network host ghcr.io/kikkia/yt-cipher:master", "docker-run")

        # 1. Push updated application.yml with youtube-source plugin
        print("\n=== Step 1: Update application.yml (add youtube-source plugin) ===")
        sftp = ssh.open_sftp()
        with sftp.file(f"{LAVALINK_DIR}/application.yml", 'w') as f:
            f.write(APPLICATION_YML)
        print("  application.yml written.")

        # 2. Fix the bot .env on the server — Lavalink runs on the SAME machine,
        #    so the bot must use 127.0.0.1, not the external IP.
        print("\n=== Step 2: Fix bot .env — set LAVALINK_URI=http://127.0.0.1:2333 ===")
        exec_remote(ssh, f"sed -i 's|LAVALINK_URI=.*|LAVALINK_URI=http://127.0.0.1:2333|' {BOT_DIR}/.env", "sed")
        # Verify
        _, out, _ = exec_remote(ssh, f"grep LAVALINK_URI {BOT_DIR}/.env", "verify")

        sftp.close()

        # 3. Restart Lavalink to pick up the new config
        print("\n=== Step 3: Restart Lavalink ===")
        exec_remote(ssh, "systemctl restart lavalink.service", "restart")
        print("  Waiting 10s for Lavalink to start and download youtube plugin...")
        time.sleep(10)
        exec_remote(ssh, "systemctl is-active lavalink.service", "status")
        exec_remote(ssh, "journalctl -u lavalink.service -n 20 --no-pager", "logs")

        # 4. Restart the bot so it picks up the fresh .env
        print("\n=== Step 4: Restart bot service ===")
        exec_remote(ssh, "systemctl restart sonarr.service", "restart")
        print("  Waiting 8s for bot to start...")
        time.sleep(8)
        exec_remote(ssh, "systemctl is-active sonarr.service", "status")
        exec_remote(ssh, "journalctl -u sonarr.service -n 25 --no-pager", "bot-logs")

        print("\n=== Done! ===")
    except Exception as e:
        print(f"\nError: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()

if __name__ == '__main__':
    main()
