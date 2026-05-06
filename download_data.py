import paramiko
from scp import SCPClient
import os

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22
REMOTE_DIR = '/root/sonarr/'
LOCAL_DIR = os.path.dirname(os.path.abspath(__file__))

PRESERVE_DATA = [
    'data/training_data.json',
    'data/context.db',
    'bot_data.db',
    'sonarr_memory.db',
    'playlists.json',
    'ai_memory.json',
    'server_config.json',
    'levels.json',
]

PRESERVE_DIRS = [
    'data/chroma_db',
    'data/setfit_model',
    'data/spacy_textcat_model',
]

def create_ssh_client(server, port, user, password):
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(server, port, user, password)
    return client

def main():
    print(f"Connecting to {HOST}:{PORT}...")
    try:
        ssh = create_ssh_client(HOST, PORT, USER, PASSWORD)
        print("Connected!")
        
        with SCPClient(ssh.get_transport()) as scp:
            for item in PRESERVE_DATA:
                remote_path = f"{REMOTE_DIR}{item}"
                local_path = os.path.join(LOCAL_DIR, item)
                
                # Create local directory if it doesn't exist
                os.makedirs(os.path.dirname(local_path), exist_ok=True)
                
                try:
                    scp.get(remote_path, local_path)
                    print(f"Downloaded file: {item}")
                except Exception as e:
                    print(f"Skipped {item} (might not exist): {e}")

            for item in PRESERVE_DIRS:
                remote_path = f"{REMOTE_DIR}{item}"
                local_path = os.path.join(LOCAL_DIR, item)
                
                # Create local directory if it doesn't exist
                os.makedirs(os.path.dirname(local_path), exist_ok=True)
                
                try:
                    scp.get(remote_path, local_path, recursive=True)
                    print(f"Downloaded directory: {item}")
                except Exception as e:
                    print(f"Skipped directory {item} (might not exist): {e}")

        print("Data download complete.")
    except Exception as e:
        print(f"Failed to connect or download: {e}")
    finally:
        if 'ssh' in locals():
            ssh.close()

if __name__ == '__main__':
    main()
