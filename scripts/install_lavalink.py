import paramiko
import sys
import os
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

def exec_remote(ssh, cmd):
    print(f"Executing: {cmd}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    
    for line in stdout:
        print(line.strip().encode("ascii", "ignore").decode("ascii"))
    
    err = stderr.read().decode("utf-8", "ignore").strip()
    if err:
        print(f"Stderr: {err}")
        
    exit_code = stdout.channel.recv_exit_status()
    print(f"Exit code: {exit_code}\\n")
    return exit_code

def main():
    print(f"Connecting to {HOST}...")
    try:
        ssh = create_ssh_client()
        print("Connected!")
        
        print("1. Installing Java 21 and curl...")
        exec_remote(ssh, "apt-get update -y && apt-get install -y openjdk-21-jre-headless curl")
        
        print(f"2. Creating directory {LAVALINK_DIR}...")
        exec_remote(ssh, f"mkdir -p {LAVALINK_DIR}")
        
        print("3. Downloading Lavalink.jar...")
        exec_remote(ssh, f"curl -L {LAVALINK_URL} -o {LAVALINK_DIR}/Lavalink.jar")
        
        print("4. Creating application.yml...")
        sftp = ssh.open_sftp()
        with sftp.file(f"{LAVALINK_DIR}/application.yml", 'w') as f:
            f.write(APPLICATION_YML)
            
        print("5. Creating lavalink.service systemd file...")
        with sftp.file("/etc/systemd/system/lavalink.service", 'w') as f:
            f.write(SERVICE_CONTENT)
        sftp.close()
        
        print("6. Reloading systemd and starting Lavalink service...")
        exec_remote(ssh, "systemctl daemon-reload")
        exec_remote(ssh, "systemctl enable lavalink.service")
        exec_remote(ssh, "systemctl restart lavalink.service")
        
        print("7. Checking Lavalink service status...")
        time.sleep(3) # Wait a moment for it to start
        exec_remote(ssh, "systemctl is-active lavalink.service")
        exec_remote(ssh, "journalctl -u lavalink.service -n 15 --no-pager")
        
        print("Lavalink installation and setup complete!")
        
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()

if __name__ == '__main__':
    main()
