import paramiko, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('192.168.1.101', 22, 'root', '12345')
cmd = 'journalctl -u lavalink.service --since "2026-05-07 12:55:00" --no-pager | grep -E "voice|channelId"'
stdin, stdout, stderr = c.exec_command(cmd)
print(stdout.read().decode('utf-8', 'ignore')[-1000:])
c.close()
