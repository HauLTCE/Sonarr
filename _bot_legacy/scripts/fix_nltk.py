import paramiko
import sys
import time

HOST = '192.168.1.101'
USER = 'root'
PASSWORD = '12345'
PORT = 22

def main():
    print(f"Connecting to {HOST}...")
    try:
        ssh = paramiko.SSHClient()
        ssh.load_system_host_keys()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, PORT, USER, PASSWORD)
        print("Connected!")
        
        cmd = "/root/sonarr/venv/bin/python -c \"import nltk; nltk.download('vader_lexicon'); nltk.download('punkt'); nltk.download('punkt_tab'); nltk.download('wordnet')\""
        print(f"Executing: {cmd}")
        stdin, stdout, stderr = ssh.exec_command(cmd)
        
        out = stdout.read().decode('utf-8', 'ignore').strip()
        err = stderr.read().decode('utf-8', 'ignore').strip()
        print(out)
        if err:
            print(f"Stderr: {err}")
            
        print("Restarting sonarr.service...")
        ssh.exec_command("systemctl restart sonarr.service")
        time.sleep(5)
        
        print("Done!")
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)
        
if __name__ == '__main__':
    main()
