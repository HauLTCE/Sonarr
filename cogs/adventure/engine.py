import math
import random
from utils.database import db
import json
import os

BASE_STATS = {
    "hp": 100,
    "max_hp": 100,
    "attack": 10,
    "defense": 5,
    "speed": 10,
    "luck": 5,
}

# ========== LOAD ENEMY DATA FROM JSON ==========
_ENEMIES_PATH = os.path.join(os.path.dirname(__file__), "enemies_data.json")
with open(_ENEMIES_PATH, "r", encoding="utf-8") as _f:
    _ENEMIES_DATA = json.load(_f)

# Enemy types — tagged for item synergies (e.g. Blackened Sword vs demon)
ENEMY_TYPES = _ENEMIES_DATA["enemy_types"]

# Floor-based enemy pools — parse "1-5" keys into range objects
ENEMIES_BY_FLOOR = {}
for _key, _enemies in _ENEMIES_DATA["enemies_by_floor"].items():
    _start, _end = map(int, _key.split("-"))
    ENEMIES_BY_FLOOR[range(_start, _end + 1)] = _enemies

# Hardest tier — used as fallback for floors beyond the table
_ENDGAME_ENEMIES = _ENEMIES_DATA["endgame_enemies"]

# Boss data
_BOSS_NAMES = {int(k): v for k, v in _ENEMIES_DATA["bosses"].items()}
_BOSS_TYPES = sorted(
    [(int(k), v) for k, v in _ENEMIES_DATA["boss_types"].items()],
    key=lambda x: x[0], reverse=True
)


def get_enemy_type(enemy_name: str) -> str:
    """Return the type tag for an enemy."""
    for etype, names in ENEMY_TYPES.items():
        if enemy_name in names:
            return etype
    return "humanoid"


def is_demon(enemy_name: str) -> bool:
    return get_enemy_type(enemy_name) == "demon"


def _scale_stat(base: float, per_floor: float, floor: int, soft_cap: int = 50) -> int:
    """Scale a stat with diminishing returns after soft_cap.
    
    Before soft_cap: linear growth (base + per_floor * floor)
    After soft_cap:  logarithmic growth for the overflow portion
    """
    if floor <= soft_cap:
        return int(base + per_floor * floor)
    linear_part = base + per_floor * soft_cap
    overflow = floor - soft_cap
    log_part = per_floor * soft_cap * 0.3 * math.log(1 + overflow / 10.0)
    return int(linear_part + log_part)


def generate_enemy(floor: int) -> dict:
    """Generate an enemy appropriate for the floor level."""
    # Pick from the highest applicable range, fallback to endgame
    candidates = _ENDGAME_ENEMIES
    for floor_range, enemies in ENEMIES_BY_FLOOR.items():
        if floor in floor_range:
            candidates = enemies
            break
    
    name = random.choice(candidates)
    
    return {
        "name": name,
        "type": get_enemy_type(name),
        "level": floor,
        "hp": _scale_stat(20, 5, floor),
        "max_hp": _scale_stat(20, 5, floor),
        "attack": _scale_stat(5, 1.5, floor),
        "defense": _scale_stat(2, 1, floor),
        "speed": _scale_stat(5, 1, floor),
        "is_boss": False,
        "bleed_stacks": 0,
    }


def generate_boss(floor: int) -> dict:
    """Generate a boss enemy for milestone floors."""
    name = _BOSS_NAMES.get(floor, f"Floor {floor} Guardian")

    # Boss type escalates with depth (from JSON thresholds, sorted desc)
    boss_type = "humanoid"
    for threshold, btype in _BOSS_TYPES:
        if floor >= threshold:
            boss_type = btype
            break

    return {
        "name": f"👑 {name}",
        "type": boss_type,
        "level": floor,
        "hp": _scale_stat(50, 10, floor),
        "max_hp": _scale_stat(50, 10, floor),
        "attack": _scale_stat(10, 2, floor),
        "defense": _scale_stat(5, 1.5, floor),
        "speed": _scale_stat(8, 1, floor),
        "is_boss": True,
        "bleed_stacks": 0,
    }


def ensure_adventure_account(user_id: int):
    uid = str(user_id)
    db.cursor.execute("SELECT 1 FROM adventure_character WHERE user_id = ?", (uid,))
    if not db.cursor.fetchone():
        db.cursor.execute('''
            INSERT INTO adventure_character (user_id, current_floor, deepest_floor, hp, max_hp, base_attack, base_defense, base_speed, base_luck)
            VALUES (?, 1, 1, ?, ?, ?, ?, ?, ?)
        ''', (uid, BASE_STATS["hp"], BASE_STATS["max_hp"], BASE_STATS["attack"], BASE_STATS["defense"], BASE_STATS["speed"], BASE_STATS["luck"]))
        db.connection.commit()


def get_character(user_id: int):
    ensure_adventure_account(user_id)
    db.cursor.execute("SELECT * FROM adventure_character WHERE user_id = ?", (str(user_id),))
    row = dict(db.cursor.fetchone())
    return row


def update_character(user_id: int, **kwargs):
    if not kwargs: return
    sets = ", ".join([f"{k} = ?" for k in kwargs.keys()])
    values = list(kwargs.values()) + [str(user_id)]
    db.cursor.execute(f"UPDATE adventure_character SET {sets} WHERE user_id = ?", values)
    db.connection.commit()


def calculate_damage(attacker_atk, defender_def, luck=0, is_crit=False, 
                     crit_chance_bonus=0.0, crit_damage_bonus=0.0, damage_bonus=0.0):
    """Calculate damage with item bonuses support."""
    base = max(1, attacker_atk - defender_def)
    variance = random.uniform(0.85, 1.15)
    damage = int(base * variance)
    
    # Apply damage bonus (e.g., Spear of the Red Dragon)
    if damage_bonus > 0:
        damage = int(damage * (1.0 + damage_bonus))
    
    if not is_crit:
        crit_chance = min(0.75, (luck / 100.0) + crit_chance_bonus)
        if random.random() < crit_chance:
            is_crit = True
            
    if is_crit:
        crit_multiplier = 1.5 + crit_damage_bonus
        damage = int(damage * crit_multiplier)
        
    return max(1, damage), is_crit


def process_bleed(enemy: dict) -> int:
    """Process bleed damage on an enemy. Returns damage dealt."""
    if enemy.get("bleed_stacks", 0) > 0:
        bleed_dmg = enemy["bleed_stacks"] * 3  # 3 damage per stack per turn
        enemy["hp"] -= bleed_dmg
        enemy["bleed_stacks"] = max(0, enemy["bleed_stacks"] - 1)
        return bleed_dmg
    return 0


def apply_on_kill_stat(user_id: int, char: dict, is_demon_enemy: bool, bonuses: dict) -> str | None:
    """Handle Blackened Sword on-kill stat gain. Returns log message or None."""
    chance = bonuses.get("on_kill_stat_chance_demon", 0) if is_demon_enemy else bonuses.get("on_kill_stat_chance", 0)
    if chance > 0 and random.random() < chance:
        stat = random.choice(["base_attack", "base_defense", "base_speed", "base_luck"])
        char[stat] += 1
        update_character(user_id, **{stat: char[stat]})
        stat_display = stat.replace("base_", "").upper()
        return f"⚫ The Blackened Sword whispers. +1 {stat_display}!"
    return None


# ========== DUNGEON LEVELING ==========

def xp_for_next_level(level: int) -> int:
    """XP required to reach the next dungeon level."""
    return 50 + (level * 25)


def award_dungeon_xp(user_id: int, char: dict, xp_amount: int) -> str | None:
    """Award dungeon XP and handle level-ups.
    
    Returns a formatted message if leveled up, None otherwise.
    """
    char['dungeon_xp'] = char.get('dungeon_xp', 0) + xp_amount
    current_level = char.get('dungeon_level', 1)
    level_ups = 0
    stat_gains = []

    while char['dungeon_xp'] >= xp_for_next_level(current_level):
        char['dungeon_xp'] -= xp_for_next_level(current_level)
        current_level += 1
        level_ups += 1

        # +5 max HP per level
        char['max_hp'] += 5
        char['hp'] = min(char['hp'] + 5, char['max_hp'])  # Heal the gained HP

        # +1 random stat
        stat = random.choice(["base_attack", "base_defense", "base_speed", "base_luck"])
        char[stat] += 1
        stat_display = stat.replace("base_", "").upper()
        stat_gains.append(f"+1 {stat_display}")

    char['dungeon_level'] = current_level

    # Save all changes
    update_character(
        user_id,
        dungeon_xp=char['dungeon_xp'],
        dungeon_level=char['dungeon_level'],
        max_hp=char['max_hp'],
        hp=char['hp'],
        base_attack=char['base_attack'],
        base_defense=char['base_defense'],
        base_speed=char['base_speed'],
        base_luck=char['base_luck'],
    )

    if level_ups > 0:
        gains_str = ", ".join(stat_gains)
        return (
            f"🎊 **LEVEL UP!** Dungeon Level {current_level - level_ups} → {current_level}\n"
            f"❤️ +{level_ups * 5} Max HP  •  {gains_str}"
        )
    return None


def calculate_xp_reward(enemy: dict, floor: int, is_boss: bool = False) -> int:
    """Calculate XP reward for defeating an enemy."""
    base_xp = enemy.get('level', 1) * 2 + floor
    if is_boss:
        base_xp *= 3
    return max(5, base_xp)

