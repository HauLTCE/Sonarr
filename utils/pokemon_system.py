import json
import logging
import random
from pathlib import Path

from utils.database import db

logger = logging.getLogger("bot")

SPECIES_FILE = Path("pokemon_species.json")
MOVES_FILE = Path("pokemon_moves.json")
ZONES_FILE = Path("pokemon_zones.json")

MAX_POKEMON_PER_USER = 3

IV_MIN = 0
IV_MAX = 31

RARITY_MULTIPLIERS = {
    "common": 1.0,
    "uncommon": 1.25,
    "rare": 1.6,
    "epic": 2.0,
    "legendary": 2.7,
}

RARITY_WEIGHTS = {
    "common": 60,
    "uncommon": 25,
    "rare": 10,
    "epic": 4,
    "legendary": 1,
}

DAILY_BASE = 100
DAILY_LEVEL_SCALE = 0.06

GAMBLE_WIN_BONUS_PER_LEVEL = 0.002
GAMBLE_MAX_WIN_BONUS = 0.12
GAMBLE_MAX_LOSS_REFUND = 0.06
GAMBLE_BET_CAP = 10000

HUNT_COST = 100
ENCOUNTER_TTL_SECONDS = 600
MAX_CATCH_ATTEMPTS = 3

DEFAULT_ZONE_ID = "meadow"
POKEDEX_MILESTONE_BONUS = {
    5: 0.05,
    10: 0.1,
    20: 0.15,
}

TRAIN_XP_MIN = 18
TRAIN_XP_MAX = 28
TRAIN_BASE_COST = 100
TRAIN_LEVEL_COST = 18

EVOLVE_BASE_COST = 1000

DAYCARE_COST_PER_HOUR = 80
DAYCARE_XP_PER_HOUR = 35

HEAL_COST_PER_HP = 3
REVIVE_BASE_COST = 600

REROLL_BASE_COST = 800
REROLL_LOCK_MULT = 2.0

BATTLE_XP_MIN = 22
BATTLE_XP_MAX = 36

WEEKLY_TARGETS = {
    "hunts": 10,
    "catches": 5,
    "levels": 8,
}

WEEKLY_REWARD_ITEMS = {
    "ultraball": 1,
    "greatball": 2,
}
WEEKLY_REWARD_DISCOUNT = 0.2
WEEKLY_REWARD_DISCOUNT_MINUTES = 4320

STARTER_SPECIES = ["bulbasaur", "charmander", "squirtle"]

DEF_LEVEL_SCALE = 0.7
MIN_DAMAGE_RANGE = (5, 10)

_species_cache = None
_species_by_rarity = None
_moves_cache = None
_zones_cache = None

TRAITS = {
    "Hardy": None,
    "Brave": ("atk", "spd"),
    "Calm": ("def", "atk"),
    "Swift": ("spd", "def"),
    "Sturdy": ("def", "spd"),
}

TYPE_CHART = {
    "fire": {"grass": 2.0, "ice": 2.0, "water": 0.5, "fire": 0.5},
    "water": {"fire": 2.0, "rock": 2.0, "water": 0.5, "grass": 0.5},
    "grass": {"water": 2.0, "rock": 2.0, "fire": 0.5, "grass": 0.5, "flying": 0.5, "poison": 0.5},
    "electric": {"water": 2.0, "flying": 2.0, "electric": 0.5, "grass": 0.5},
    "ice": {"grass": 2.0, "flying": 2.0, "water": 0.5, "fire": 0.5, "ice": 0.5},
    "flying": {"grass": 2.0, "electric": 0.5},
    "poison": {"grass": 2.0, "poison": 0.5},
    "normal": {"rock": 0.5, "ghost": 0.0, "steel": 0.5},
    "dark": {"psychic": 2.0, "ghost": 2.0, "dark": 0.5, "fighting": 0.5, "fairy": 0.5},
    "dragon": {"dragon": 2.0},
    "psychic": {"poison": 2.0},
}


def get_pokemon_capacity(owner_id):
    bonus = db.get_pokemon_capacity_bonus(str(owner_id))
    return MAX_POKEMON_PER_USER + max(0, int(bonus))


def load_species_data():
    global _species_cache, _species_by_rarity
    if _species_cache is not None:
        return _species_cache

    if not SPECIES_FILE.exists():
        logger.warning("[Pokemon] pokemon_species.json not found")
        _species_cache = {}
        _species_by_rarity = {}
        return _species_cache

    try:
        with SPECIES_FILE.open("r", encoding="utf-8") as f:
            data = json.load(f)
            if isinstance(data, dict):
                _species_cache = data
            else:
                _species_cache = {}
    except Exception as e:
        logger.error(f"[Pokemon] Failed to load species data: {e}")
        _species_cache = {}

    _species_by_rarity = None
    return _species_cache


def load_moves_data():
    global _moves_cache
    if _moves_cache is not None:
        return _moves_cache

    if not MOVES_FILE.exists():
        logger.warning("[Pokemon] pokemon_moves.json not found")
        _moves_cache = {}
        return _moves_cache

    try:
        with MOVES_FILE.open("r", encoding="utf-8") as f:
            data = json.load(f)
            if isinstance(data, dict):
                _moves_cache = data
            else:
                _moves_cache = {}
    except Exception as e:
        logger.error(f"[Pokemon] Failed to load moves data: {e}")
        _moves_cache = {}
    return _moves_cache


def load_zones_data():
    global _zones_cache
    if _zones_cache is not None:
        return _zones_cache

    if not ZONES_FILE.exists():
        logger.warning("[Pokemon] pokemon_zones.json not found")
        _zones_cache = {}
        return _zones_cache

    try:
        with ZONES_FILE.open("r", encoding="utf-8") as f:
            data = json.load(f)
            if isinstance(data, dict):
                _zones_cache = data
            else:
                _zones_cache = {}
    except Exception as e:
        logger.error(f"[Pokemon] Failed to load zones data: {e}")
        _zones_cache = {}
    return _zones_cache


def get_species(species_id):
    data = load_species_data()
    return data.get(species_id)


def get_move(move_id):
    data = load_moves_data()
    return data.get(move_id)


def get_zone(zone_id):
    data = load_zones_data()
    return data.get(zone_id)


def list_zones():
    data = load_zones_data()
    return list(data.keys())


def get_species_name(species_id):
    data = load_species_data()
    entry = data.get(species_id, {})
    return entry.get("name", species_id)


def get_species_by_rarity():
    global _species_by_rarity
    if _species_by_rarity is not None:
        return _species_by_rarity
    data = load_species_data()
    by_rarity = {}
    for sid, entry in data.items():
        rarity = entry.get("rarity", "common")
        by_rarity.setdefault(rarity, []).append(sid)
    _species_by_rarity = by_rarity
    return by_rarity


def apply_encounter_bonus(weights, bonus):
    if bonus <= 0:
        return weights
    boosted = dict(weights)
    for rarity in ("rare", "epic", "legendary"):
        if rarity in boosted:
            boosted[rarity] = int(boosted[rarity] * (1 + bonus))
    return boosted


def roll_wild_level(rarity):
    ranges = {
        "common": (1, 4),
        "uncommon": (2, 5),
        "rare": (4, 7),
        "epic": (6, 10),
        "legendary": (8, 12),
    }
    low, high = ranges.get(rarity, (1, 3))
    return random.randint(low, high)


def roll_wild_level_for_zone(rarity, zone):
    if not zone:
        return roll_wild_level(rarity)
    level_ranges = zone.get("level_ranges", {})
    if rarity in level_ranges:
        low, high = level_ranges[rarity]
        return random.randint(int(low), int(high))
    return roll_wild_level(rarity)


def choose_wild_species(encounter_bonus=0.0):
    data = load_species_data()
    if not data:
        return None, None, None

    by_rarity = get_species_by_rarity()
    weights = apply_encounter_bonus(RARITY_WEIGHTS, encounter_bonus)
    rarities = list(weights.keys())
    rarity = random.choices(rarities, weights=[weights[r] for r in rarities], k=1)[0]
    choices = by_rarity.get(rarity) or list(data.keys())
    species_id = random.choice(choices)
    return species_id, data.get(species_id), rarity


def choose_wild_species_for_zone(zone, encounter_bonus=0.0):
    data = load_species_data()
    if not data:
        return None, None, None
    pool_ids = zone.get("species_pool") or []
    if pool_ids:
        pool = {sid: data[sid] for sid in pool_ids if sid in data}
    else:
        pool = data

    by_rarity = {}
    for sid, entry in pool.items():
        rarity = entry.get("rarity", "common")
        by_rarity.setdefault(rarity, []).append(sid)

    weights = zone.get("rarity_weights", RARITY_WEIGHTS)
    weights = apply_encounter_bonus(weights, encounter_bonus)
    weights = {rarity: weights[rarity] for rarity in weights if rarity in by_rarity}
    if not weights:
        weights = {rarity: RARITY_WEIGHTS.get(rarity, 1) for rarity in by_rarity}
    if not weights:
        return None, None, None

    rarities = list(weights.keys())
    rarity = random.choices(rarities, weights=[weights[r] for r in rarities], k=1)[0]
    choices = by_rarity.get(rarity) or list(pool.keys())
    species_id = random.choice(choices)
    return species_id, data.get(species_id), rarity


def get_rarity_multiplier(rarity):
    return RARITY_MULTIPLIERS.get(rarity, 1.0)


def calc_daily_income(level, rarity):
    rarity_mult = get_rarity_multiplier(rarity)
    level_factor = 1.0 + (level * DAILY_LEVEL_SCALE)
    return max(0, int(DAILY_BASE * rarity_mult * level_factor))


def calc_gamble_bonus(level, rarity):
    rarity_mult = get_rarity_multiplier(rarity)
    raw = level * GAMBLE_WIN_BONUS_PER_LEVEL * rarity_mult
    win_bonus = min(GAMBLE_MAX_WIN_BONUS, raw)
    loss_refund = min(GAMBLE_MAX_LOSS_REFUND, raw * 0.6)
    return win_bonus, loss_refund


def get_gamble_modifiers(owner_id):
    pokemon = db.get_equipped_pokemon(str(owner_id))
    if not pokemon or pokemon.get("is_fainted"):
        return None, 0.0, 0.0
    win_bonus, loss_refund = calc_gamble_bonus(pokemon["level"], pokemon["rarity"])
    return pokemon, win_bonus, loss_refund


def calc_win_bonus(amount, win_pct):
    return max(0, int(amount * win_pct))


def calc_loss_refund(amount, loss_pct):
    return max(0, int(amount * loss_pct))


def xp_cap(level):
    return 50 + (25 * level) + (level * level * 5)


def apply_xp(level, xp, gain):
    total_xp = xp + gain
    levels_gained = 0
    while total_xp >= xp_cap(level):
        total_xp -= xp_cap(level)
        level += 1
        levels_gained += 1
    return level, total_xp, levels_gained


def calc_train_cost(level, rarity):
    base = TRAIN_BASE_COST + (level * TRAIN_LEVEL_COST)
    return max(0, int(base * get_rarity_multiplier(rarity)))


def roll_ivs():
    return {
        "hp": random.randint(IV_MIN, IV_MAX),
        "atk": random.randint(IV_MIN, IV_MAX),
        "def": random.randint(IV_MIN, IV_MAX),
        "spd": random.randint(IV_MIN, IV_MAX),
    }


def roll_trait():
    return random.choice(list(TRAITS.keys()))


def apply_trait(stats, trait):
    mod = TRAITS.get(trait)
    if not mod:
        return stats
    plus, minus = mod
    updated = dict(stats)
    updated[plus] = max(1, int(updated[plus] * 1.1))
    updated[minus] = max(1, int(updated[minus] * 0.9))
    return updated


def calc_stats(species, level, ivs, trait):
    base = species.get("base_stats", {"hp": 40, "atk": 40, "def": 40, "spd": 40})
    stats = {
        "hp": base.get("hp", 40) + ivs.get("hp", 0) + (level * 2),
        "atk": base.get("atk", 40) + ivs.get("atk", 0) + level,
        "def": base.get("def", 40) + ivs.get("def", 0) + (level * DEF_LEVEL_SCALE),
        "spd": base.get("spd", 40) + ivs.get("spd", 0) + level,
    }
    stats = {key: max(1, int(value)) for key, value in stats.items()}
    return apply_trait(stats, trait)


def get_available_moves(species_id, level):
    species = get_species(species_id) or {}
    pool = species.get("move_pool", [])
    if not pool:
        return []
    eligible = [m for m in pool if int(m.get("level", 0)) <= level]
    eligible.sort(key=lambda m: int(m.get("level", 0)))
    seen = set()
    selected = []
    for entry in reversed(eligible):
        move_id = entry.get("move_id")
        if move_id and move_id not in seen:
            move = get_move(move_id)
            if move:
                selected.append({"id": move_id, **move})
                seen.add(move_id)
        if len(selected) >= 4:
            break
    return list(reversed(selected))


def type_multiplier(move_type, defender_types):
    multiplier = 1.0
    chart = TYPE_CHART.get(move_type, {})
    for dtype in defender_types:
        multiplier *= chart.get(dtype, 1.0)
    return multiplier


def calc_damage(attacker_stats, defender_stats, move, attacker_types, defender_types, attacker_level=1):
    power = move.get("power", 0)
    if power <= 0:
        return 0
    accuracy = move.get("accuracy", 100)
    if random.randint(1, 100) > accuracy:
        return 0
    stab = 1.5 if move.get("type") in attacker_types else 1.0
    effectiveness = type_multiplier(move.get("type"), defender_types)
    if effectiveness == 0:
        return 0

    level = max(1, int(attacker_level))
    attack = max(1.0, float(attacker_stats.get("atk", 1)))
    defense = max(1.0, float(defender_stats.get("def", 1)))
    base = (((2 * level / 5) + 2) * power * (attack / defense)) / 50 + 2
    damage = base * stab * effectiveness
    damage *= random.uniform(0.85, 1.0)
    min_damage = random.randint(MIN_DAMAGE_RANGE[0], MIN_DAMAGE_RANGE[1])
    return max(min_damage, int(damage))


def calc_run_chance(player_spd, enemy_spd, attempts=0):
    ratio = player_spd / max(1, enemy_spd)
    bonus = min(0.2, attempts * 0.05)
    chance = 0.35 + (0.4 * ratio) + bonus
    return max(0.1, min(0.95, chance))


def get_pokedex_bonus(unique_count):
    bonus = 0.0
    for threshold, value in sorted(POKEDEX_MILESTONE_BONUS.items()):
        if unique_count >= threshold:
            bonus = value
    return bonus


def calc_heal_cost(missing_hp, rarity):
    return max(0, int(missing_hp * HEAL_COST_PER_HP * get_rarity_multiplier(rarity)))


def calc_revive_cost(level, rarity):
    return max(0, int((REVIVE_BASE_COST + (level * 20)) * get_rarity_multiplier(rarity)))


def calc_daycare_cost(hours, rarity):
    return max(0, int(hours * DAYCARE_COST_PER_HOUR * get_rarity_multiplier(rarity)))


def calc_daycare_xp(hours, rarity):
    base = hours * DAYCARE_XP_PER_HOUR
    return max(0, int(base * get_rarity_multiplier(rarity)))


def calc_battle_xp(level, rarity):
    base = random.randint(BATTLE_XP_MIN, BATTLE_XP_MAX)
    return max(1, int(base + (level * 2) * get_rarity_multiplier(rarity)))


def calc_reroll_cost(level, rarity, locked=False):
    base = REROLL_BASE_COST + (level * 40)
    cost = base * get_rarity_multiplier(rarity)
    if locked:
        cost *= REROLL_LOCK_MULT
    return max(0, int(cost))


def apply_fee_discount(cost, discount):
    if not discount:
        return cost
    return max(0, int(cost * (1 - float(discount))))
