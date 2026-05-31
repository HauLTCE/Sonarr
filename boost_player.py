"""
Boost a specific player on the remote server.
- Give all legendary items (equipped)
- Set HP to 10000, max_hp to 10000
- Set all base stats to 1000
- Give 100 of every consumable

Uses SSH to run SQL on the remote bot_data.db, same as deploy.py.
"""
import paramiko
import os
import sys
from dotenv import load_dotenv

load_dotenv()

# ============ CONFIG (from environment — see .env.example) ============
# Secrets are NEVER hardcoded. Reuses the same DEPLOY_* vars as deploy.py.
HOST = os.getenv('DEPLOY_HOST')
USER = os.getenv('DEPLOY_USER', 'root')
PASSWORD = os.getenv('DEPLOY_PASSWORD')
PORT = int(os.getenv('DEPLOY_PORT', '22'))
REMOTE_DIR = os.getenv('DEPLOY_REMOTE_DIR', '/root/sonarr/')
DB_PATH = f'{REMOTE_DIR}bot_data.db'

if not HOST or not PASSWORD:
    sys.exit("ERROR: DEPLOY_HOST and DEPLOY_PASSWORD must be set in .env")

TARGET_USER_ID = os.getenv('BOOST_TARGET_USER_ID', '594006837230305280')

# All legendary item IDs from items_data.json
# Ordered so blackened_sword is first → gets equipped as weapon
LEGENDARY_ITEMS = {
    "blackened_sword":      {"slot": "weapon",    "stats": '{"attack": 30, "speed": 5}'},
    "crown_of_madness":     {"slot": "helmet",    "stats": '{"attack": 20, "luck": 10, "defense": -5}'},
    "crown_of_the_endless": {"slot": "accessory", "stats": '{"defense": 20, "luck": 15, "max_hp": 50}'},
    "ring_of_the_void":     {"slot": "ring",      "stats": '{"attack": 15, "defense": 15, "luck": 15}'},
    "hourglass_archiver":   {"slot": "accessory", "stats": '{"defense": 10, "luck": 15}'},
    "spear_red_dragon":     {"slot": "weapon",    "stats": '{"attack": 35}'},
    "starforgers_favor":    {"slot": "accessory", "stats": '{"luck": 25, "attack": 10}'},
    "dagger_and_pike":      {"slot": "weapon",    "stats": '{"attack": 22, "speed": 10}'},
    "banshees_call":        {"slot": "weapon",    "stats": '{"attack": 18, "speed": 8}'},
}

# All consumable IDs — loaded from JSON
import json as _json
_CONS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "utils", "consumables_data.json")
with open(_CONS_PATH, "r", encoding="utf-8") as _f:
    CONSUMABLES = list(_json.load(_f).keys())


def create_ssh_client():
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(HOST, PORT, USER, PASSWORD)
    return client


def exec_remote(ssh, cmd, label=""):
    if label:
        print(f"  [{label}] {cmd[:120]}")
    stdin, stdout, stderr = ssh.exec_command(cmd)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode().strip()
    err = stderr.read().decode().strip()
    if out:
        print(f"  {out}")
    if err and exit_code != 0:
        print(f"  ERROR: {err}")
    return exit_code, out, err


def build_sql():
    """Build the full SQL script to boost the player."""
    lines = []

    # 1. Ensure character exists, then update stats
    lines.append(f"""
INSERT INTO adventure_character (user_id, current_floor, deepest_floor, hp, max_hp, base_attack, base_defense, base_speed, base_luck)
VALUES ('{TARGET_USER_ID}', 1, 1, 10000, 10000, 1000, 1000, 1000, 1000)
ON CONFLICT(user_id) DO UPDATE SET
    hp = 10000,
    max_hp = 10000,
    base_attack = 1000,
    base_defense = 1000,
    base_speed = 1000,
    base_luck = 1000;
""")

    # 2. Clear existing inventory for this user (clean slate for legendaries)
    lines.append(f"DELETE FROM adventure_inventory WHERE user_id = '{TARGET_USER_ID}';")

    # 3. Insert all legendary items — equip one per slot, rest unequipped
    equipped_slots = set()
    for item_id, info in LEGENDARY_ITEMS.items():
        slot = info["slot"]
        stats_json = info["stats"]
        # Equip the first item per slot
        equip = 1 if slot not in equipped_slots else 0
        if equip:
            equipped_slots.add(slot)
        lines.append(
            f"INSERT INTO adventure_inventory (user_id, item_id, slot, rarity, stats_json, equipped, quantity) "
            f"VALUES ('{TARGET_USER_ID}', '{item_id}', '{slot}', 'legendary', '{stats_json}', {equip}, 1);"
        )

    # 4. Give 100 of every consumable
    for cid in CONSUMABLES:
        lines.append(
            f"INSERT INTO consumable_inventory (user_id, item_id, quantity) "
            f"VALUES ('{TARGET_USER_ID}', '{cid}', 100) "
            f"ON CONFLICT(user_id, item_id) DO UPDATE SET quantity = 100;"
        )

    return "\n".join(lines)


def main():
    print("=" * 60)
    print("SONARR — Boost Player")
    print(f"Target: {TARGET_USER_ID}")
    print("=" * 60)

    sql = build_sql()

    try:
        print(f"\nConnecting to {HOST}:{PORT}...")
        ssh = create_ssh_client()
        print("Connected!\n")

        # Stop service to get clean DB access
        print("[1] Stopping service...")
        exec_remote(ssh, 'systemctl stop sonarr.service', "stop")
        import time
        time.sleep(2)

        # Flush WAL
        print("[2] Flushing WAL...")
        flush_cmd = (
            f'cd {REMOTE_DIR} && '
            'venv/bin/python3 -c "import sqlite3; c=sqlite3.connect(\'bot_data.db\'); '
            'c.execute(\'PRAGMA wal_checkpoint(TRUNCATE)\'); c.close(); print(\'WAL flushed\')"'
        )
        exec_remote(ssh, flush_cmd, "flush")

        # Write a Python script to remote that does both exec + verify
        print("[3] Applying boost...")
        remote_script = f"""import sqlite3
c = sqlite3.connect('{REMOTE_DIR}bot_data.db')
sql = open('/tmp/boost_player.sql').read()
c.executescript(sql)
c.close()

# Verify
c = sqlite3.connect('{REMOTE_DIR}bot_data.db')
r = c.execute("SELECT hp,max_hp,base_attack,base_defense,base_speed,base_luck FROM adventure_character WHERE user_id='{TARGET_USER_ID}'").fetchone()
print(f"  Stats: HP={{r[0]}}/{{r[1]}} ATK={{r[2]}} DEF={{r[3]}} SPD={{r[4]}} LCK={{r[5]}}")
n = c.execute("SELECT COUNT(*) FROM adventure_inventory WHERE user_id='{TARGET_USER_ID}'").fetchone()[0]
print(f"  Items: {{n}} legendary")
n2 = c.execute("SELECT COUNT(*) FROM consumable_inventory WHERE user_id='{TARGET_USER_ID}' AND quantity=100").fetchone()[0]
print(f"  Consumables: {{n2}} types x100")
c.close()
print("OK")
"""
        # Write SQL file
        write_sql_cmd = f"cat << 'ENDSQL' > /tmp/boost_player.sql\n{sql}\nENDSQL"
        exec_remote(ssh, write_sql_cmd, "write SQL")

        # Write Python script
        write_py_cmd = f"cat << 'ENDPY' > /tmp/boost_run.py\n{remote_script}\nENDPY"
        exec_remote(ssh, write_py_cmd, "write script")

        # Execute
        exit_code, _, _ = exec_remote(ssh, f'cd {REMOTE_DIR} && venv/bin/python3 /tmp/boost_run.py', "run")

        if exit_code == 0:
            print("  ✅ Boost applied successfully!")
        else:
            print("  ❌ Execution failed!")
            sys.exit(1)

        # Cleanup
        exec_remote(ssh, 'rm -f /tmp/boost_player.sql /tmp/boost_run.py', "cleanup")

        # Restart service
        print("\n[5] Restarting service...")
        exec_remote(ssh, 'systemctl start sonarr.service', "start")
        time.sleep(3)
        exec_remote(ssh, 'systemctl is-active sonarr.service', "status")

        print("\n" + "=" * 60)
        print("BOOST COMPLETE!")
        print(f"User {TARGET_USER_ID} now has:")
        print(f"  • 10,000 HP / 10,000 Max HP")
        print(f"  • 1,000 ATK / DEF / SPD / LCK")
        print(f"  • {len(LEGENDARY_ITEMS)} legendary items")
        print(f"  • {len(CONSUMABLES)} consumables × 100 each")
        print("=" * 60)

    except Exception as e:
        print(f"\nError: {e}")
        sys.exit(1)
    finally:
        if 'ssh' in locals():
            ssh.close()


if __name__ == '__main__':
    main()
