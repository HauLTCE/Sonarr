import paramiko, sys, time
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('192.168.1.101', 22, 'root', '12345')

# Wait for full startup
time.sleep(15)

# Check bot connected
cmd = 'journalctl -u sonarr.service -n 5 --no-pager'
stdin, stdout, stderr = c.exec_command(cmd)
print("=== BOT STARTUP ===")
print(stdout.read().decode('utf-8', 'ignore')[-500:])

# Check lavalink status
cmd2 = 'journalctl -u lavalink.service -n 5 --no-pager'
stdin, stdout, stderr = c.exec_command(cmd2)
print("\n=== LAVALINK STATUS ===")
print(stdout.read().decode('utf-8', 'ignore')[-500:])

# Wait 90 seconds for stability test
print("\nWaiting 90 seconds for websocket stability test...")
time.sleep(90)

# Check if websocket survived
cmd3 = 'journalctl -u lavalink.service --since "2026-05-07 13:08:00" --no-pager | grep -c "1006"'
stdin, stdout, stderr = c.exec_command(cmd3)
disconnects = stdout.read().decode().strip()
print(f"\n=== WEBSOCKET 1006 DISCONNECTIONS SINCE RESTART: {disconnects} ===")

cmd4 = 'journalctl -u lavalink.service -n 3 --no-pager'
stdin, stdout, stderr = c.exec_command(cmd4)
print("\n=== LATEST LAVALINK ===")
print(stdout.read().decode('utf-8', 'ignore')[-500:])

c.close()
