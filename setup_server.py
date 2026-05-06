import paramiko
import time
import sys

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22

SERVICE_CONTENT = """[Unit]
Description=Sonarr AI Discord Bot
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=/root/sonarr
ExecStart=/root/sonarr/venv/bin/python main.py
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
    
    # Read output as it comes
    for line in stdout:
        print(line.strip())
    
    err = stderr.read().decode().strip()
    if err:
        print(f"Stderr: {err}")
        
    exit_code = stdout.channel.recv_exit_status()
    print(f"Exit code: {exit_code}\n")
    return exit_code

def main():
    print(f"Connecting to {HOST}...")
    try:
        ssh = create_ssh_client()
        print("Connected!")
        
        # 1. Update and install required packages
        exec_remote(ssh, "apt-get update -y && apt-get install -y python3 python3-pip python3-venv ffmpeg tar wget curl git")
        
        # 2. Create the sonarr directory
        exec_remote(ssh, "mkdir -p /root/sonarr")
        
        # 3. Create the systemd service file
        sftp = ssh.open_sftp()
        with sftp.file('/etc/systemd/system/sonarr.service', 'w') as f:
            f.write(SERVICE_CONTENT)
        sftp.close()
        
        # 4. Reload systemd
        exec_remote(ssh, "systemctl daemon-reload")
        exec_remote(ssh, "systemctl enable sonarr.service")
        
        print("Server setup complete! You can now run deploy.py.")
        
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()

if __name__ == '__main__':
    main()
