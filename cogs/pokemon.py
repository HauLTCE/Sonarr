import discord
from discord.ext import commands
import json
import os
import random
import logging
import math
import re
from datetime import datetime, timezone, timedelta, time as datetime_time
from types import SimpleNamespace
from uuid import uuid4

from .pokemon_views import (
    WildBattleView,
    DuelBattleView,
    PokemonMenuView,
    PokemonActionPickerView,
    PokemonExtraModal,
    PokemonTextModal,
    PokemonConfirmView,
)
from utils.economy import EconomyManager
from utils.checks import economy_allowed, BotRestrictedTimeError
from utils.database import db
from utils.pokemon_system import (
    get_pokemon_capacity,
    DEFAULT_ZONE_ID,
    GAMBLE_BET_CAP,
    HUNT_COST,
    ENCOUNTER_TTL_SECONDS,
    MAX_CATCH_ATTEMPTS,
    TRAIN_XP_MIN,
    TRAIN_XP_MAX,
    EVOLVE_BASE_COST,
    STARTER_SPECIES,
    WEEKLY_TARGETS,
    WEEKLY_REWARD_ITEMS,
    WEEKLY_REWARD_DISCOUNT,
    WEEKLY_REWARD_DISCOUNT_MINUTES,
    choose_wild_species_for_zone,
    roll_wild_level_for_zone,
    get_species,
    get_species_name,
    get_zone,
    list_zones,
    get_rarity_multiplier,
    calc_daily_income,
    calc_gamble_bonus,
    calc_train_cost,
    apply_xp,
    roll_ivs,
    roll_trait,
    calc_stats,
    get_available_moves,
    get_move,
    type_multiplier,
    calc_damage,
    xp_cap,
    calc_heal_cost,
    calc_revive_cost,
    calc_daycare_cost,
    calc_daycare_xp,
    calc_reroll_cost,
    get_pokedex_bonus,
    apply_fee_discount,
)

logger = logging.getLogger("bot")

ITEMS_FILE = "items.json"


class Pokemon(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        self.items = self.load_items()
        self.buff_name_map = self._build_buff_name_map()

    def load_items(self):
        if not os.path.exists(ITEMS_FILE):
            logger.warning("[Pokemon] items.json not found")
            return {}
        try:
            with open(ITEMS_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
                if isinstance(data, dict):
                    return data
        except Exception as e:
            logger.error(f"[Pokemon] Failed to load items: {e}")
        return {}

    def _build_buff_name_map(self):
        mapping = {}
        for item in self.items.values():
            buff_id = item.get("buff_id")
            if buff_id:
                mapping[buff_id] = item.get("name", buff_id)
        return mapping

    def find_item(self, query):
        key = query.lower().strip()
        if key in self.items:
            return key, self.items[key]
        for item_id, item in self.items.items():
            name = str(item.get("name", "")).lower()
            if name == key:
                return item_id, item
        return None, None

    def _daily_reset_times(self):
        now_utc = datetime.now(timezone.utc)
        tz = timezone(timedelta(hours=7))
        now_tz = now_utc.astimezone(tz)
        today = now_tz.date().isoformat()
        return now_tz, today, tz

    def _week_start(self):
        now = datetime.now(timezone.utc)
        start = now - timedelta(days=now.weekday())
        return start.date().isoformat()

    def _get_weekly(self, owner_id):
        week_start = self._week_start()
        data = db.get_weekly(owner_id)
        if not data or data["week_start"] != week_start:
            db.set_weekly(owner_id, week_start, 0, 0, 0, False)
            return {
                "week_start": week_start,
                "hunts": 0,
                "catches": 0,
                "levels": 0,
                "reward_claimed": False,
            }
        return data

    def _update_weekly(self, owner_id, field, delta):
        data = self._get_weekly(owner_id)
        hunts = int(data["hunts"])
        catches = int(data["catches"])
        levels = int(data["levels"])
        if field == "hunts":
            hunts += delta
        elif field == "catches":
            catches += delta
        elif field == "levels":
            levels += delta
        db.set_weekly(owner_id, data["week_start"], hunts, catches, levels, data["reward_claimed"])

    def _apply_discount(self, cost, owner_id):
        discount = db.get_active_buff(owner_id, "fee_discount") or 0
        return apply_fee_discount(cost, discount)

    async def _send_message(self, target, content=None, embed=None, view=None, ephemeral=False):
        if isinstance(target, discord.Interaction):
            if target.response.is_done():
                return await target.followup.send(content=content, embed=embed, view=view, ephemeral=ephemeral)
            return await target.response.send_message(content=content, embed=embed, view=view, ephemeral=ephemeral)
        return await target.send(content=content, embed=embed, view=view)

    def _progress_bar(self, current, total, length=8):
        if total <= 0:
            return "░" * length
        ratio = max(0.0, min(1.0, float(current) / float(total)))
        filled = int(round(ratio * length))
        return "█" * filled + "░" * (length - filled)

    def _format_duration(self, seconds):
        if seconds <= 0:
            return "expired"
        hours, rem = divmod(int(seconds), 3600)
        minutes, secs = divmod(rem, 60)
        if hours > 0:
            return f"{hours}h {minutes}m"
        if minutes > 0:
            return f"{minutes}m {secs}s"
        return f"{secs}s"

    def _format_active_buffs(self, owner_id):
        buffs = db.get_active_buffs(owner_id)
        if not buffs:
            return "None"
        now = datetime.now(timezone.utc).timestamp()
        lines = []
        for buff in buffs:
            buff_id = buff["buff_id"]
            name = self.buff_name_map.get(buff_id, buff_id)
            value = buff.get("value", 0)
            prefix = "-" if buff_id == "fee_discount" else "+"
            pct = f"{prefix}{int(round(value * 100))}%"
            remaining = self._format_duration(buff["expires_at"] - now)
            lines.append(f"{name}: {pct} ({remaining})")
        return "\n".join(lines)

    def _interaction_ctx(self, interaction, ephemeral=True):
        async def send(content=None, embed=None, view=None):
            return await self._send_message(
                interaction,
                content=content,
                embed=embed,
                view=view,
                ephemeral=ephemeral,
            )

        return SimpleNamespace(author=interaction.user, send=send, guild=interaction.guild)

    def _get_capacity(self, owner_id):
        return get_pokemon_capacity(owner_id)

    def build_ball_options(self, owner_id):
        uid = str(owner_id)
        options = []
        for item_id, item in self.items.items():
            if item.get("type") != "pokemon_ball":
                continue
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                continue
            bonus = int(round(float(item.get("catch_bonus", 0.0)) * 100))
            desc = f"+{bonus}% catch" if bonus else "Standard catch rate"
            label = f"{item.get('name', item_id)} x{qty}"
            options.append(discord.SelectOption(label=label, value=item_id, description=desc))
        return options

    def build_zone_options(self, owner_id):
        uid = str(owner_id)
        unique = db.get_pokedex_unique_count(uid)
        options = []
        for zone_id in list_zones():
            zone = get_zone(zone_id)
            if not zone:
                continue
            unlock_req = int(zone.get("unlock_species", 0))
            locked = unique < unlock_req
            label = zone.get("name", zone_id)
            cost = int(zone.get("hunt_cost", HUNT_COST))
            if locked:
                desc = f"Locked ({unlock_req} species)"
            else:
                desc = f"{zone_id} | Cost ${cost}"
            options.append(discord.SelectOption(label=label, value=zone_id, description=desc))
        return options

    def build_action_options(self, owner_id):
        uid = str(owner_id)
        owned_count = db.count_owned_pokemon(uid)
        options = [
            discord.SelectOption(
                label="⚔️ Resume Battle",
                value="battle",
                description="Continue the current wild battle",
            ),
            discord.SelectOption(
                label="🎁 Weekly Claim",
                value="weekly_claim",
                description="Claim weekly mission rewards",
            ),
            discord.SelectOption(
                label="🧪 Train Pokemon",
                value="train",
                description="Spend money to gain XP",
            ),
            discord.SelectOption(
                label="✨ Evolve Pokemon",
                value="evolve",
                description="Evolve if eligible",
            ),
            discord.SelectOption(
                label="💊 Heal Pokemon",
                value="heal",
                description="Restore HP with item or cash",
            ),
            discord.SelectOption(
                label="💖 Revive Pokemon",
                value="revive",
                description="Revive a fainted pokemon",
            ),
            discord.SelectOption(
                label="🎲 Reroll IVs",
                value="reroll",
                description="Reroll IVs/trait (optional lock)",
            ),
            discord.SelectOption(
                label="🏫 Daycare Add",
                value="daycare_add",
                description="Send pokemon to daycare",
            ),
            discord.SelectOption(
                label="📋 Daycare Status",
                value="daycare_status",
                description="View daycare progress",
            ),
            discord.SelectOption(
                label="🎁 Daycare Claim",
                value="daycare_claim",
                description="Claim daycare XP",
            ),
            discord.SelectOption(
                label="🧱 Upgrade Capacity",
                value="upgrade",
                description="Use a PC upgrade item",
            ),
            discord.SelectOption(
                label="🗑️ Release Pokemon",
                value="release",
                description="Release a pokemon you own",
            ),
            discord.SelectOption(
                label="🤝 Duel Player",
                value="duel",
                description="Challenge another player",
            ),
            discord.SelectOption(
                label="🔍 Pokemon Info",
                value="info",
                description="View details by ID",
            ),
            discord.SelectOption(
                label="⭐ Equip Pokemon",
                value="equip",
                description="Equip a pokemon by ID",
            ),
        ]
        if owned_count == 0:
            options.append(
                discord.SelectOption(
                    label="⭐ Choose Starter",
                    value="starter",
                    description="Pick your first pokemon",
                )
            )
        return options

    def build_pokemon_select_options(self, owner_id, page=0, per_page=23):
        uid = str(owner_id)
        owned = db.get_owned_pokemon(uid)
        if not owned:
            return [], 0, 0, 0

        owned_sorted = sorted(owned, key=lambda p: (not p.get("is_equipped"), p.get("created_at", 0)))
        total = len(owned_sorted)
        total_pages = max(1, int(math.ceil(total / float(per_page))))
        page = max(0, min(int(page), total_pages - 1))

        start = page * per_page
        end = start + per_page
        slice_items = owned_sorted[start:end]

        options = []
        for pokemon in slice_items:
            species = get_species_name(pokemon["species_id"])
            label = f"{species} (Lv {pokemon['level']})"
            if len(label) > 100:
                label = label[:97] + "..."
            desc = f"id:{pokemon['pokemon_id']} | {pokemon['rarity']}"
            options.append(discord.SelectOption(label=label, value=pokemon["pokemon_id"], description=desc))

        if total_pages > 1:
            if page > 0:
                options.append(
                    discord.SelectOption(
                        label="⬅️ Prev Page",
                        value="__prev__",
                        description=f"Page {page}/{total_pages}",
                    )
                )
            if page < total_pages - 1:
                options.append(
                    discord.SelectOption(
                        label="➡️ Next Page",
                        value="__next__",
                        description=f"Page {page + 2}/{total_pages}",
                    )
                )

        return options, page, total_pages, total

    def _format_rarity(self, rarity):
        return rarity.capitalize()

    def _format_pokemon_line(self, p, state=None):
        name = get_species_name(p["species_id"])
        tag = "⭐" if p["is_equipped"] else "•"
        status = " 💀" if p.get("is_fainted") else ""
        hp_text = ""
        xp_text = ""
        if state:
            hp_bar = self._progress_bar(state["current_hp"], state["max_hp"], length=8)
            xp_bar = self._progress_bar(state["xp"], xp_cap(state["level"]), length=6)
            hp_text = f" | HP {state['current_hp']}/{state['max_hp']} {hp_bar}"
            xp_text = f" | XP {state['xp']}/{xp_cap(state['level'])} {xp_bar}"
        return (
            f"{tag} {name} | id:{p['pokemon_id']} | lvl:{p['level']} | "
            f"{self._format_rarity(p['rarity'])}{hp_text}{xp_text} {status}"
        ).strip()

    def _prepare_pokemon_state(self, owner_id, pokemon):
        owner_id_str = str(owner_id)
        species = get_species(pokemon["species_id"]) or {}
        ivs = pokemon.get("ivs") or {}
        trait = pokemon.get("trait")
        if not ivs:
            ivs = roll_ivs()
        if not trait:
            trait = roll_trait()
        if not pokemon.get("ivs") or not pokemon.get("trait"):
            db.update_pokemon_ivs_trait(owner_id_str, pokemon["pokemon_id"], ivs, trait)

        stats = calc_stats(species, pokemon["level"], ivs, trait)
        max_hp = stats["hp"]
        current_hp = pokemon.get("current_hp", 0)
        if current_hp <= 0 and not pokemon.get("is_fainted"):
            current_hp = max_hp
            db.update_pokemon_hp(owner_id_str, pokemon["pokemon_id"], current_hp)
        if current_hp > max_hp:
            current_hp = max_hp
            db.update_pokemon_hp(owner_id_str, pokemon["pokemon_id"], current_hp)

        moves = get_available_moves(pokemon["species_id"], pokemon["level"])
        if not moves:
            fallback = get_move("tackle")
            if fallback:
                moves = [{"id": "tackle", **fallback}]

        return {
            "owner_id": owner_id,
            "owner_name": None,
            "pokemon_id": pokemon["pokemon_id"],
            "species_id": pokemon["species_id"],
            "rarity": pokemon["rarity"],
            "level": pokemon["level"],
            "xp": pokemon["xp"],
            "ivs": ivs,
            "trait": trait,
            "current_hp": current_hp,
            "max_hp": max_hp,
            "stats": stats,
            "types": species.get("types", ["normal"]),
            "moves": moves,
        }

    def _prepare_wild_state(self, encounter):
        species = get_species(encounter["species_id"]) or {}
        ivs = {"hp": 10, "atk": 10, "def": 10, "spd": 10}
        trait = "Hardy"
        stats = calc_stats(species, encounter["level"], ivs, trait)
        max_hp = encounter.get("max_hp") or stats["hp"]
        current_hp = encounter.get("current_hp") or max_hp
        moves = get_available_moves(encounter["species_id"], encounter["level"])
        if not moves:
            fallback = get_move("tackle")
            if fallback:
                moves = [{"id": "tackle", **fallback}]
        return {
            "species_id": encounter["species_id"],
            "rarity": encounter["rarity"],
            "level": encounter["level"],
            "current_hp": current_hp,
            "max_hp": max_hp,
            "stats": stats,
            "types": species.get("types", ["normal"]),
            "moves": moves,
        }

    def _ensure_pokemon_genetics(self, owner_id, pokemon):
        ivs = pokemon.get("ivs") or {}
        trait = pokemon.get("trait")
        updated = False
        if not ivs:
            ivs = roll_ivs()
            updated = True
        if not trait:
            trait = roll_trait()
            updated = True
        if updated:
            db.update_pokemon_ivs_trait(str(owner_id), pokemon["pokemon_id"], ivs, trait)
        return ivs, trait

    def _apply_hp_delta(self, owner_id, pokemon, new_level=None, new_species_id=None):
        if pokemon.get("is_fainted"):
            return
        current_hp = pokemon.get("current_hp", 0)
        if current_hp <= 0:
            return
        ivs, trait = self._ensure_pokemon_genetics(owner_id, pokemon)
        old_species = get_species(pokemon["species_id"]) or {}
        old_level = pokemon["level"]
        old_stats = calc_stats(old_species, old_level, ivs, trait)

        target_species_id = new_species_id or pokemon["species_id"]
        target_level = new_level if new_level is not None else old_level
        new_species = get_species(target_species_id) or {}
        new_stats = calc_stats(new_species, target_level, ivs, trait)

        delta = new_stats["hp"] - old_stats["hp"]
        if delta == 0:
            return
        new_hp = current_hp + delta
        new_hp = min(new_stats["hp"], max(1, new_hp))
        db.update_pokemon_hp(str(owner_id), pokemon["pokemon_id"], new_hp)

    def build_party_embed(self, owner_id):
        uid = str(owner_id)
        owned = db.get_owned_pokemon(uid)
        capacity = self._get_capacity(uid)
        embed = discord.Embed(
            title="🎒 Pokemon Party",
            color=0x2ecc71,
        )
        if not owned:
            embed.description = "You have no pokemon yet. Use `!pokemon starter` or tap 🧭 Hunt."
            embed.add_field(name="Party", value="Empty", inline=False)
            embed.add_field(name="✨ Active Buffs", value=self._format_active_buffs(uid), inline=False)
            return embed

        lines = []
        for p in owned:
            state = self._prepare_pokemon_state(uid, p)
            lines.append(self._format_pokemon_line(p, state))
        embed.add_field(
            name=f"Party ({len(owned)}/{capacity})",
            value="\n".join(lines),
            inline=False,
        )
        embed.add_field(name="✨ Active Buffs", value=self._format_active_buffs(uid), inline=False)
        embed.set_footer(text="Tip: Tap a Pokemon button below to view info.")
        return embed

    def build_help_embed(self):
        embed = discord.Embed(title="🆘 Pokemon Guide", color=0x2ecc71)
        embed.add_field(
            name="🚀 Getting Started",
            value=(
                "`!pokemon starter <name>` Pick your starter\n"
                "`!pokemon` View your party\n"
                "`!pokemon info <id>` Detailed stats\n"
                "`!pokemon equip <id>` Equip a pokemon"
            ),
            inline=False,
        )
        embed.add_field(
            name="🧭 Hunting & Battle",
            value=(
                "`!pokemon zones` View zones\n"
                "`!pokemon hunt [zone]` Start a wild encounter\n"
                "`!pokemon battle` Resume a wild battle\n"
                "`!pokemon catch [ball]` Throw a ball\n"
                "`!pokemon duel @user` Duel another player"
            ),
            inline=False,
        )
        embed.add_field(
            name="📈 Progression",
            value=(
                "`!pokemon train <id> [sessions]` Train for XP\n"
                "`!pokemon evolve <id> [target]` Evolve if eligible\n"
                "`!pokemon reroll <id> [stat]` Reroll IVs/trait\n"
                "`!pokemon daycare add <id> <hours>` Daycare training"
            ),
            inline=False,
        )
        embed.add_field(
            name="💊 Recovery & Rewards",
            value=(
                "`!pokemon heal <id> [item]` Heal HP\n"
                "`!pokemon revive <id> [item]` Revive fainted\n"
                "`!pokemon daily` Claim daily income\n"
                "`!pokemon weekly` / `!pokemon weekly_claim` Weekly missions"
            ),
            inline=False,
        )
        embed.add_field(
            name="🧩 Other",
            value=(
                "`!pokemon pokedex` Milestones & bonuses\n"
                "`!pokemon release <id>` Release a pokemon\n"
                "`!pokemon upgrade [item]` Increase party capacity"
            ),
            inline=False,
        )
        embed.set_footer(text="Use 🧰 Actions in the menu to run commands without typing.")
        return embed

    def build_actions_embed(self, owner_id):
        embed = discord.Embed(title="🧰 Pokemon Actions", color=0x2ecc71)
        embed.description = "Pick an action from the dropdown below. You'll choose a pokemon from a list when needed."
        embed.add_field(
            name="Quick Tips",
            value="Use the Party view to browse your pokemon and tap a button or dropdown to select one.",
            inline=False,
        )
        return embed

    def build_action_picker_embed(self, owner_id, action, page, total_pages, total_count):
        action_labels = {
            "info": "🔍 Pokemon Info",
            "equip": "⭐ Equip Pokemon",
            "train": "🧪 Train Pokemon",
            "evolve": "✨ Evolve Pokemon",
            "heal": "💊 Heal Pokemon",
            "revive": "💖 Revive Pokemon",
            "reroll": "🎲 Reroll IVs",
            "release": "🗑️ Release Pokemon",
            "daycare_add": "🏫 Daycare Add",
        }
        title = action_labels.get(action, f"{action.title()} Pokemon")
        embed = discord.Embed(title=title, color=0x2ecc71)
        if total_count == 0:
            embed.description = "You have no pokemon yet. Use `!pokemon starter` or Hunt to get one."
            return embed
        if total_pages > 1:
            embed.description = f"Select a pokemon. Page {page + 1}/{total_pages}."
        else:
            embed.description = "Select a pokemon."
        return embed

    def build_cost_confirmation_embed(self, title, description, cost):
        embed = discord.Embed(title=title, color=0xf1c40f)
        embed.description = description
        embed.add_field(name="Cost", value=f"${cost}", inline=True)
        embed.set_footer(text="Press Confirm to proceed or Cancel to go back.")
        return embed

    def _preview_train_plan(self, owner_id, pokemon, sessions):
        if pokemon.get("is_fainted"):
            return {"error": "That pokemon is fainted. Revive it first."}

        if sessions <= 0:
            return {"error": "Sessions must be positive."}
        sessions = min(sessions, 25)

        wallet = self.economy_manager.get_balance(str(owner_id), "wallet")
        total_cost = 0
        total_xp = 0
        levels_gained = 0
        performed = 0
        level = pokemon["level"]
        xp = pokemon["xp"]
        rarity = pokemon["rarity"]

        for _ in range(sessions):
            cost = calc_train_cost(level, rarity)
            cost = self._apply_discount(cost, owner_id)
            if wallet < total_cost + cost:
                break
            gain = random.randint(TRAIN_XP_MIN, TRAIN_XP_MAX)
            level, xp, gained = apply_xp(level, xp, gain)
            total_cost += cost
            total_xp += gain
            levels_gained += gained
            performed += 1

        if total_cost == 0:
            return {"error": "Not enough money to train."}

        return {
            "sessions": sessions,
            "performed": performed,
            "total_cost": total_cost,
            "total_xp": total_xp,
            "levels_gained": levels_gained,
            "final_level": level,
            "final_xp": xp,
            "start_level": pokemon["level"],
            "start_xp": pokemon["xp"],
        }

    def _preview_evolve(self, owner_id, pokemon, target):
        uid = str(owner_id)
        species = get_species(pokemon["species_id"]) or {}
        evolves_to = species.get("evolves_to")
        evolve_level = int(species.get("evolve_level", 0) or 0)
        evolve_item = species.get("evolve_item")
        evolve_options = species.get("evolve_options", [])

        new_species_id = None
        required_item = None

        if evolve_options:
            if not target:
                options = ", ".join([f"{opt['species_id']} ({opt['item_id']})" for opt in evolve_options])
                return {"error": f"Choose evolution: {options}"}
            target = target.lower().strip()
            chosen = None
            for opt in evolve_options:
                if opt.get("species_id") == target:
                    chosen = opt
                    break
            if not chosen:
                return {"error": "Invalid evolution choice."}
            item_id = chosen.get("item_id")
            if item_id:
                qty = db.get_inventory_item(uid, item_id)
                if qty <= 0:
                    return {"error": f"You need {item_id} to evolve."}
            required_item = item_id
            new_species_id = chosen.get("species_id")
        elif evolve_item:
            qty = db.get_inventory_item(uid, evolve_item)
            if qty <= 0:
                return {"error": f"You need {evolve_item} to evolve."}
            required_item = evolve_item
            new_species_id = evolves_to
        else:
            if not evolves_to:
                return {"error": "This pokemon cannot evolve."}
            if pokemon["level"] < evolve_level:
                return {"error": f"Need level {evolve_level} to evolve."}
            new_species_id = evolves_to

        if not new_species_id:
            return {"error": "Evolution data missing."}

        new_species = get_species(new_species_id) or {}
        new_rarity = new_species.get("rarity", pokemon["rarity"])
        cost = int(EVOLVE_BASE_COST * get_rarity_multiplier(new_rarity))
        cost = self._apply_discount(cost, uid)

        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return {"error": f"You need ${cost} to evolve."}

        return {
            "new_species_id": new_species_id,
            "new_rarity": new_rarity,
            "required_item": required_item,
            "cost": cost,
            "target": target,
        }

    def _preview_heal(self, owner_id, pokemon, item_name):
        uid = str(owner_id)
        if pokemon.get("is_fainted"):
            return {"error": "That pokemon is fainted. Use `!pokemon revive`."}

        state = self._prepare_pokemon_state(uid, pokemon)
        missing = state["max_hp"] - state["current_hp"]
        if missing <= 0:
            return {"error": "That pokemon is already at full HP."}

        if item_name:
            item_id, item = self.find_item(item_name)
            if not item or item.get("type") != "pokemon_heal":
                return {"error": "Invalid heal item."}
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                return {"error": f"You do not have any {item.get('name', item_id)}."}
            heal_amount = int(item.get("heal_amount", 0))
            if heal_amount <= 0:
                return {"error": "This item cannot heal."}
            return {"cost": 0, "item_id": item_id, "item_name": item.get("name", item_id)}

        cost = calc_heal_cost(missing, pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return {"error": f"You need ${cost} to heal."}
        return {"cost": cost}

    def _preview_revive(self, owner_id, pokemon, item_name):
        uid = str(owner_id)
        if not pokemon.get("is_fainted"):
            return {"error": "That pokemon is not fainted."}

        if item_name:
            item_id, item = self.find_item(item_name)
            if not item or item.get("type") != "pokemon_revive":
                return {"error": "Invalid revive item."}
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                return {"error": f"You do not have any {item.get('name', item_id)}."}
            revive_pct = float(item.get("revive_percent", 0.0))
            if revive_pct <= 0:
                return {"error": "This item cannot revive."}
            return {"cost": 0, "item_id": item_id, "item_name": item.get("name", item_id)}

        cost = calc_revive_cost(pokemon["level"], pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return {"error": f"You need ${cost} to revive."}
        return {"cost": cost}

    def _preview_reroll(self, owner_id, pokemon, lock_stat):
        uid = str(owner_id)
        lock_stat = lock_stat.lower().strip() if lock_stat else None
        if lock_stat and lock_stat not in {"hp", "atk", "def", "spd"}:
            return {"error": "Lock stat must be one of: hp, atk, def, spd."}

        cost = calc_reroll_cost(pokemon["level"], pokemon["rarity"], locked=bool(lock_stat))
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return {"error": f"You need ${cost} to reroll."}
        return {"cost": cost, "lock_stat": lock_stat}

    def _preview_daycare(self, owner_id, pokemon, hours):
        uid = str(owner_id)
        if pokemon.get("is_fainted"):
            return {"error": "That pokemon is fainted."}
        if hours <= 0 or hours > 24:
            return {"error": "Hours must be between 1 and 24."}
        existing = db.get_daycare_entries(uid)
        for entry in existing:
            if entry["pokemon_id"] == pokemon["pokemon_id"]:
                return {"error": "That pokemon is already in daycare."}

        cost = calc_daycare_cost(hours, pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return {"error": f"You need ${cost} to use daycare."}
        return {"cost": cost, "hours": hours}

    def build_pokedex_embed(self, owner_id):
        uid = str(owner_id)
        unique = db.get_pokedex_unique_count(uid)
        bonus = get_pokedex_bonus(unique)
        embed = discord.Embed(title="📘 Pokedex", color=0x3498db)
        embed.add_field(name="✅ Unique Caught", value=str(unique), inline=True)
        embed.add_field(name="✨ Rare Encounter Bonus", value=f"+{int(bonus * 100)}%", inline=True)
        embed.add_field(name="🏁 Milestones", value="5 / 10 / 20 unique species", inline=False)
        return embed

    def build_weekly_embed(self, owner_id):
        uid = str(owner_id)
        data = self._get_weekly(uid)
        embed = discord.Embed(title="📅 Weekly Missions", color=0x1abc9c)
        embed.add_field(name="🔎 Hunts", value=f"{data['hunts']}/{WEEKLY_TARGETS['hunts']}", inline=True)
        embed.add_field(name="🎯 Catches", value=f"{data['catches']}/{WEEKLY_TARGETS['catches']}", inline=True)
        embed.add_field(name="⬆️ Levels", value=f"{data['levels']}/{WEEKLY_TARGETS['levels']}", inline=True)
        embed.add_field(
            name="🎁 Reward",
            value="Claim with `!pokemon weekly_claim` when complete.",
            inline=False,
        )
        return embed

    def build_zones_embed(self, owner_id):
        uid = str(owner_id)
        unique = db.get_pokedex_unique_count(uid)
        embed = discord.Embed(title="🗺️ Pokemon Zones", color=0x3498db)
        for zone_id in list_zones():
            zone = get_zone(zone_id)
            if not zone:
                continue
            unlock_req = int(zone.get("unlock_species", 0))
            status = "Unlocked ✅" if unique >= unlock_req else f"Locked 🔒 ({unlock_req} species)"
            pool = zone.get("species_pool") or []
            pool_note = f"Pool: {len(pool)} species" if pool else "Pool: global"
            cooldown_seconds = int(zone.get("cooldown_seconds", 0))
            cooldown_text = "none" if cooldown_seconds <= 0 else f"{cooldown_seconds}s"
            embed.add_field(
                name=f"{zone.get('name', zone_id)} ({zone_id})",
                value=(
                    f"Cost: ${zone.get('hunt_cost', HUNT_COST)}\n"
                    f"Cooldown: {cooldown_text}\n"
                    f"{status}\n"
                    f"{pool_note}"
                ),
                inline=False,
            )
        if not embed.fields:
            embed.description = "No zones configured."
        return embed

    def build_info_embed(self, owner_id, pokemon):
        uid = str(owner_id)
        species = get_species(pokemon["species_id"]) or {}
        name = species.get("name", pokemon["species_id"])
        rarity = pokemon["rarity"]
        level = pokemon["level"]
        xp = pokemon["xp"]
        daily_income = calc_daily_income(level, rarity)
        win_bonus, loss_refund = calc_gamble_bonus(level, rarity)

        state = self._prepare_pokemon_state(uid, pokemon)
        ivs = state["ivs"]
        trait = state["trait"]
        stats = state["stats"]
        types = ", ".join([t.capitalize() for t in state["types"]])
        moves = state["moves"]
        move_lines = []
        for move in moves:
            move_type = move.get("type", "unknown")
            move_type = move_type.capitalize() if move_type else "Unknown"
            move_lines.append(
                f"{move.get('name', 'Move')} ({move_type}, P{move.get('power', 0)}, Acc {move.get('accuracy', 100)}%)"
            )
        if not move_lines:
            move_lines = ["None"]

        embed = discord.Embed(title=f"🔍 Pokemon Info: {name}", color=0x2ecc71)
        hp_bar = self._progress_bar(state["current_hp"], state["max_hp"], length=12)
        xp_bar = self._progress_bar(xp, xp_cap(level), length=12)
        embed.add_field(
            name="📌 Summary",
            value=(
                f"ID: {pokemon['pokemon_id']}\n"
                f"Rarity: {self._format_rarity(rarity)}\n"
                f"Types: {types}\n"
                f"Level: {level}\n"
                f"XP: {xp}/{xp_cap(level)} {xp_bar}\n"
                f"HP: {state['current_hp']}/{state['max_hp']} {hp_bar}"
            ),
            inline=False,
        )
        embed.add_field(
            name="📊 Stats",
            value=(
                f"HP {stats['hp']} | ATK {stats['atk']} | DEF {stats['def']} | SPD {stats['spd']}\n"
                f"IVs: HP {ivs.get('hp', 0)} | ATK {ivs.get('atk', 0)} | DEF {ivs.get('def', 0)} | SPD {ivs.get('spd', 0)}\n"
                f"Trait: {trait}"
            ),
            inline=False,
        )
        embed.add_field(name="🧠 Moves", value="\n".join(move_lines), inline=False)
        embed.add_field(
            name="💰 Bonuses",
            value=(
                f"Daily income: ${daily_income}\n"
                f"Gamble bonus: +{round(win_bonus * 100, 2)}% win, "
                f"{round(loss_refund * 100, 2)}% refund (bet cap ${GAMBLE_BET_CAP})"
            ),
            inline=False,
        )
        embed.add_field(name="✨ Active Buffs", value=self._format_active_buffs(uid), inline=False)
        return embed

    def build_daily_embed(self, name, income):
        embed = discord.Embed(title="🎯 Daily Income", color=0xf1c40f)
        embed.add_field(name="Pokemon", value=name, inline=True)
        embed.add_field(name="Income", value=f"${income}", inline=True)
        return embed

    def build_daily_cooldown_embed(self, remaining):
        embed = discord.Embed(title="⏳ Daily Cooldown", color=0xe67e22)
        embed.description = f"Come back in {remaining} for pokemon income."
        return embed

    def _claim_daily(self, owner_id):
        uid = str(owner_id)
        equipped = db.get_equipped_pokemon(uid)
        if not equipped:
            return {"error": "Equip a pokemon first with `!pokemon equip <id>`."}

        now_tz, today, tz = self._daily_reset_times()
        last = db.get_pokemon_daily(uid)
        if last == today:
            next_reset = datetime.combine(now_tz.date() + timedelta(days=1), datetime_time.min, tzinfo=tz)
            remaining = next_reset - now_tz
            total_seconds = int(remaining.total_seconds())
            return {"cooldown": self._format_duration(total_seconds)}

        income = calc_daily_income(equipped["level"], equipped["rarity"])
        self.economy_manager.update_balance(uid, income, "wallet")
        db.set_pokemon_daily(uid, today)
        name = get_species_name(equipped["species_id"])
        return {"income": income, "name": name}

    def _preview_hunt(self, owner_id, zone_id=None):
        uid = str(owner_id)
        zone_id = (zone_id or DEFAULT_ZONE_ID).lower()
        zone = get_zone(zone_id)
        if not zone:
            return {"error": "Zone not found. Use `!pokemon zones`."}

        unique = db.get_pokedex_unique_count(uid)
        unlock_req = int(zone.get("unlock_species", 0))
        if unique < unlock_req:
            return {"error": f"You need {unlock_req} unique species to hunt in this zone."}

        wallet = self.economy_manager.get_balance(uid, "wallet")
        base_cost = int(zone.get("hunt_cost", HUNT_COST))
        cost = self._apply_discount(base_cost, uid)
        if wallet < cost:
            return {"error": f"You need ${cost} in your wallet to hunt."}
        capacity = self._get_capacity(uid)
        if db.count_owned_pokemon(uid) >= capacity:
            return {"error": f"You already have {capacity} pokemon. Release one first."}

        equipped = db.get_equipped_pokemon(uid)
        if not equipped:
            return {"error": "Equip a pokemon first with `!pokemon equip <id>`."}
        if equipped.get("is_fainted"):
            return {"error": "Your equipped pokemon is fainted. Use `!pokemon revive`."}

        return {"zone_id": zone_id, "zone": zone, "cost": cost}

    def _prepare_hunt(self, owner_id, zone_id=None):
        uid = str(owner_id)
        zone_id = (zone_id or DEFAULT_ZONE_ID).lower()
        zone = get_zone(zone_id)
        if not zone:
            return {"error": "Zone not found. Use `!pokemon zones`."}

        unique = db.get_pokedex_unique_count(uid)
        unlock_req = int(zone.get("unlock_species", 0))
        if unique < unlock_req:
            return {"error": f"You need {unlock_req} unique species to hunt in this zone."}

        now = datetime.now(timezone.utc).timestamp()
        wallet = self.economy_manager.get_balance(uid, "wallet")
        base_cost = int(zone.get("hunt_cost", HUNT_COST))
        cost = self._apply_discount(base_cost, uid)
        if wallet < cost:
            return {"error": f"You need ${cost} in your wallet to hunt."}
        capacity = self._get_capacity(uid)
        if db.count_owned_pokemon(uid) >= capacity:
            return {"error": f"You already have {capacity} pokemon. Release one first."}

        equipped = db.get_equipped_pokemon(uid)
        if not equipped:
            return {"error": "Equip a pokemon first with `!pokemon equip <id>`."}
        if equipped.get("is_fainted"):
            return {"error": "Your equipped pokemon is fainted. Use `!pokemon revive`."}

        encounter = db.get_pokemon_encounter(uid)
        if encounter and encounter["expires_at"] > now:
            species_name = get_species_name(encounter["species_id"])
            intro = f"You already found {species_name} (lvl {encounter['level']}). Resuming battle."
            return {"encounter": encounter, "intro": intro}
        if encounter:
            db.clear_pokemon_encounter(uid)

        encounter_bonus = db.get_active_buff(uid, "encounter_boost") or 0
        milestone_bonus = get_pokedex_bonus(unique)
        species_id, species, rarity = choose_wild_species_for_zone(zone, encounter_bonus + milestone_bonus)
        if not species_id:
            return {"error": "No pokemon data loaded."}

        level = roll_wild_level_for_zone(rarity, zone)
        stats = calc_stats(species, level, {"hp": 10, "atk": 10, "def": 10, "spd": 10}, "Hardy")
        max_hp = stats["hp"]
        expires_at = now + ENCOUNTER_TTL_SECONDS
        db.set_pokemon_encounter(uid, species_id, rarity, level, max_hp, max_hp, zone_id, expires_at, attempts=0)
        self.economy_manager.update_balance(uid, -cost, "wallet")
        self._update_weekly(uid, "hunts", 1)

        name = species.get("name", species_id)
        zone_name = zone.get("name", zone_id)
        intro = (
            f"[{zone_name}] You encountered {name} (lvl {level}, {self._format_rarity(rarity)}). "
            f"Fight, catch, or run within {ENCOUNTER_TTL_SECONDS // 60} minutes."
        )
        return {"encounter": db.get_pokemon_encounter(uid), "intro": intro}

    async def handle_hunt_interaction(self, interaction, zone_id, origin="main"):
        uid = str(interaction.user.id)
        now = datetime.now(timezone.utc).timestamp()
        encounter = db.get_pokemon_encounter(uid)
        if encounter and encounter["expires_at"] > now:
            return await self._start_wild_battle(interaction, encounter, uid)
        if encounter:
            db.clear_pokemon_encounter(uid)

        preview = self._preview_hunt(uid, zone_id)
        if preview.get("error"):
            return await self._send_message(interaction, preview["error"], ephemeral=True)

        zone = preview.get("zone") or {}
        zone_name = zone.get("name", preview.get("zone_id", zone_id))
        desc = f"Hunt in {zone_name} ({preview.get('zone_id', zone_id)})."
        embed = self.build_cost_confirmation_embed("🧭 Confirm Hunt", desc, preview["cost"])
        return_mode = "zones" if origin == "zones" else "party"
        payload = {
            "action": "hunt",
            "zone_id": preview.get("zone_id", zone_id),
            "cost": preview["cost"],
            "return_mode": return_mode,
        }
        view = PokemonConfirmView(self, interaction.user.id, payload)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=view)
        else:
            await interaction.response.edit_message(embed=embed, view=view)
        view.message = interaction.message
        return

    async def handle_daily_interaction(self, interaction):
        uid = str(interaction.user.id)
        result = self._claim_daily(uid)
        if result.get("error"):
            return await self._send_message(interaction, result["error"], ephemeral=True)
        if result.get("cooldown"):
            embed = self.build_daily_cooldown_embed(result["cooldown"])
            return await self._send_message(interaction, embed=embed, ephemeral=True)
        embed = self.build_daily_embed(result["name"], result["income"])
        return await self._send_message(interaction, embed=embed, ephemeral=True)

    async def handle_battle_interaction(self, interaction):
        uid = str(interaction.user.id)
        encounter = db.get_pokemon_encounter(uid)
        if not encounter:
            return await self._send_message(interaction, "No wild pokemon found. Use `!pokemon hunt`.", ephemeral=True)
        if encounter["current_hp"] <= 0:
            return await self._send_message(interaction, "The wild pokemon has fainted.", ephemeral=True)
        await self._start_wild_battle(interaction, encounter, uid)

    async def handle_action_select(self, interaction, action):
        pokemon_actions = {
            "info",
            "equip",
            "train",
            "evolve",
            "heal",
            "revive",
            "reroll",
            "release",
            "daycare_add",
        }
        if action in pokemon_actions:
            return await self.show_action_picker(interaction, action, origin="actions")
        if action == "starter":
            return await interaction.response.send_modal(
                PokemonTextModal(
                    self,
                    "starter",
                    "Choose Starter",
                    "Starter species",
                    "e.g. bulbasaur",
                )
            )
        if action == "upgrade":
            return await interaction.response.send_modal(
                PokemonTextModal(
                    self,
                    "upgrade",
                    "Upgrade Capacity",
                    "Item name (optional)",
                    "pc_upgrade",
                    required=False,
                )
            )
        if action == "duel":
            return await interaction.response.send_modal(
                PokemonTextModal(
                    self,
                    "duel",
                    "Pokemon Duel",
                    "Opponent @mention or ID",
                    "@player or 1234567890",
                )
            )
        if action == "battle":
            return await self.handle_battle_interaction(interaction)
        if action == "weekly_claim":
            ctx = self._interaction_ctx(interaction)
            return await self.pokemon_weekly_claim(ctx)
        if action == "daycare_status":
            ctx = self._interaction_ctx(interaction)
            return await self.pokemon_daycare_status(ctx)
        if action == "daycare_claim":
            ctx = self._interaction_ctx(interaction)
            return await self.pokemon_daycare_claim(ctx)

        return await self._send_message(interaction, "Action not supported yet.", ephemeral=True)

    async def show_action_picker(self, interaction, action, origin="actions", page=0):
        owner_id = interaction.user.id
        options, page, total_pages, total_count = self.build_pokemon_select_options(owner_id, page=page)
        embed = self.build_action_picker_embed(owner_id, action, page, total_pages, total_count)
        new_view = PokemonActionPickerView(self, owner_id, action, page=page, origin=origin)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=new_view)
        else:
            await interaction.response.edit_message(embed=embed, view=new_view)
        new_view.message = interaction.message

    async def handle_action_with_pokemon(self, interaction, action, pokemon_id, origin="actions", page=0):
        uid = str(interaction.user.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await self._send_message(interaction, "Pokemon not found.", ephemeral=True)

        if action == "info":
            embed = self.build_info_embed(uid, pokemon)
            view = PokemonMenuView(self, interaction.user.id)
            if interaction.response.is_done():
                await interaction.message.edit(embed=embed, view=view)
            else:
                await interaction.response.edit_message(embed=embed, view=view)
            view.message = interaction.message
            return

        if action == "equip":
            db.set_equipped_pokemon(uid, pokemon_id)
            name = get_species_name(pokemon["species_id"])
            embed = self.build_party_embed(uid)
            view = PokemonMenuView(self, interaction.user.id, show_party_buttons=True)
            if interaction.response.is_done():
                await interaction.message.edit(embed=embed, view=view)
            else:
                await interaction.response.edit_message(embed=embed, view=view)
            view.message = interaction.message
            await interaction.followup.send(f"Equipped {name} (id:{pokemon_id}).", ephemeral=True)
            return

        if action == "train":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "train",
                    pokemon_id,
                    origin,
                    page,
                    "Train Pokemon",
                    "Sessions (1-25, optional)",
                    "e.g. 3",
                )
            )
        if action == "evolve":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "evolve",
                    pokemon_id,
                    origin,
                    page,
                    "Evolve Pokemon",
                    "Target species (optional)",
                    "e.g. ivysaur",
                )
            )
        if action == "heal":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "heal",
                    pokemon_id,
                    origin,
                    page,
                    "Heal Pokemon",
                    "Item (optional)",
                    "e.g. potion",
                )
            )
        if action == "revive":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "revive",
                    pokemon_id,
                    origin,
                    page,
                    "Revive Pokemon",
                    "Item (optional)",
                    "e.g. revive",
                )
            )
        if action == "reroll":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "reroll",
                    pokemon_id,
                    origin,
                    page,
                    "Reroll IVs",
                    "Lock stat (optional)",
                    "hp / atk / def / spd",
                )
            )
        if action == "release":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "release",
                    pokemon_id,
                    origin,
                    page,
                    "Release Pokemon",
                    "Type RELEASE to confirm",
                    "RELEASE",
                    extra_required=True,
                )
            )
        if action == "daycare_add":
            return await interaction.response.send_modal(
                PokemonExtraModal(
                    self,
                    "daycare_add",
                    pokemon_id,
                    origin,
                    page,
                    "Daycare Add",
                    "Hours (1-24)",
                    "e.g. 6",
                    extra_required=True,
                )
            )

        return await self._send_message(interaction, "Action not supported yet.", ephemeral=True)

    async def handle_modal_action(self, interaction, action, pokemon_id, extra=None, origin="actions", page=0):
        uid = str(interaction.user.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await self._send_message(interaction, "Pokemon not found.", ephemeral=True)

        ctx = self._interaction_ctx(interaction)
        extra_value = (extra or "").strip()

        if action == "release":
            if extra_value.lower() != "release":
                return await self._send_message(interaction, "Release cancelled. Type RELEASE to confirm.", ephemeral=True)
            return await self.pokemon_release(ctx, pokemon_id)

        if action == "train":
            sessions = 1
            if extra_value:
                try:
                    sessions = int(extra_value)
                except ValueError:
                    return await self._send_message(interaction, "Sessions must be a number.", ephemeral=True)
            plan = self._preview_train_plan(uid, pokemon, sessions)
            if plan.get("error"):
                return await self._send_message(interaction, plan["error"], ephemeral=True)
            name = get_species_name(pokemon["species_id"])
            desc = (
                f"Train {name} for {plan['performed']}/{plan['sessions']} sessions.\n"
                f"Expected XP: {plan['total_xp']} (+{plan['levels_gained']} levels)."
            )
            embed = self.build_cost_confirmation_embed("🧪 Confirm Training", desc, plan["total_cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": plan["total_cost"],
                "plan": plan,
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        if action == "evolve":
            target = extra_value or None
            preview = self._preview_evolve(uid, pokemon, target)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            old_name = get_species_name(pokemon["species_id"])
            new_name = get_species_name(preview["new_species_id"])
            desc = f"Evolve {old_name} → {new_name}."
            if preview.get("required_item"):
                desc += f"\nRequires item: {preview['required_item']}."
            embed = self.build_cost_confirmation_embed("✨ Confirm Evolution", desc, preview["cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": preview["cost"],
                "target": preview.get("target"),
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        if action == "heal":
            item_name = extra_value or None
            preview = self._preview_heal(uid, pokemon, item_name)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost", 0) <= 0:
                return await self.pokemon_heal(ctx, pokemon_id, item_name)
            name = get_species_name(pokemon["species_id"])
            desc = f"Heal {name} to full HP."
            embed = self.build_cost_confirmation_embed("💊 Confirm Heal", desc, preview["cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": preview["cost"],
                "item_name": item_name,
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        if action == "revive":
            item_name = extra_value or None
            preview = self._preview_revive(uid, pokemon, item_name)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost", 0) <= 0:
                return await self.pokemon_revive(ctx, pokemon_id, item_name)
            name = get_species_name(pokemon["species_id"])
            desc = f"Revive {name} from fainted."
            embed = self.build_cost_confirmation_embed("💖 Confirm Revive", desc, preview["cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": preview["cost"],
                "item_name": item_name,
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        if action == "reroll":
            preview = self._preview_reroll(uid, pokemon, extra_value or None)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            name = get_species_name(pokemon["species_id"])
            lock_note = f" (lock: {preview['lock_stat']})" if preview.get("lock_stat") else ""
            desc = f"Reroll IVs and trait for {name}{lock_note}."
            embed = self.build_cost_confirmation_embed("🎲 Confirm Reroll", desc, preview["cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": preview["cost"],
                "lock_stat": preview.get("lock_stat"),
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        if action == "daycare_add":
            try:
                hours = int(extra_value)
            except ValueError:
                return await self._send_message(interaction, "Hours must be a number.", ephemeral=True)
            preview = self._preview_daycare(uid, pokemon, hours)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            name = get_species_name(pokemon["species_id"])
            desc = f"Send {name} to daycare for {preview['hours']}h."
            embed = self.build_cost_confirmation_embed("🏫 Confirm Daycare", desc, preview["cost"])
            payload = {
                "action": action,
                "pokemon_id": pokemon_id,
                "origin": origin,
                "page": page,
                "cost": preview["cost"],
                "hours": preview["hours"],
                "return_mode": "dismiss",
            }
            view = PokemonConfirmView(self, interaction.user.id, payload)
            message = await self._send_message(interaction, embed=embed, view=view, ephemeral=True)
            if message:
                view.message = message
            return

        return await self._send_message(interaction, "Action not supported yet.", ephemeral=True)

    async def handle_text_action(self, interaction, action, text):
        ctx = self._interaction_ctx(interaction)
        value = (text or "").strip()
        if action == "starter":
            if not value:
                return await self._send_message(interaction, "Starter species is required.", ephemeral=True)
            return await self.pokemon_starter(ctx, value)
        if action == "upgrade":
            item_name = value or "pc_upgrade"
            return await self.pokemon_upgrade(ctx, item_name)
        if action == "duel":
            member = await self._resolve_member(interaction.guild, value)
            if not member:
                return await self._send_message(interaction, "Opponent not found. Mention them or use their ID.", ephemeral=True)
            return await self._start_duel(ctx, member)
        return await self._send_message(interaction, "Action not supported yet.", ephemeral=True)

    async def handle_cost_confirm(self, interaction, payload):
        action = payload.get("action")
        uid = str(interaction.user.id)
        ctx = self._interaction_ctx(interaction)

        if action == "hunt":
            zone_id = payload.get("zone_id")
            preview = self._preview_hunt(uid, zone_id)
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            result = self._prepare_hunt(uid, zone_id)
            if result.get("error"):
                return await self._send_message(interaction, result["error"], ephemeral=True)
            return await self._start_wild_battle(interaction, result["encounter"], uid, intro_line=result["intro"])

        pokemon_id = payload.get("pokemon_id")
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await self._send_message(interaction, "Pokemon not found.", ephemeral=True)

        if action == "train":
            plan = payload.get("plan") or {}
            if pokemon.get("is_fainted"):
                return await self._send_message(interaction, "That pokemon is fainted. Revive it first.", ephemeral=True)
            if (
                pokemon["level"] != plan.get("start_level")
                or pokemon["xp"] != plan.get("start_xp")
            ):
                return await self._send_message(interaction, "Pokemon changed. Please try again.", ephemeral=True)
            cost = int(plan.get("total_cost", 0))
            wallet = self.economy_manager.get_balance(uid, "wallet")
            if wallet < cost:
                return await self._send_message(interaction, f"You need ${cost} to train.", ephemeral=True)

            self.economy_manager.update_balance(uid, -cost, "wallet")
            db.update_pokemon_progress(uid, pokemon_id, plan["final_level"], plan["final_xp"])
            if plan.get("levels_gained", 0) > 0:
                self._update_weekly(uid, "levels", plan["levels_gained"])
                self._apply_hp_delta(uid, pokemon, new_level=plan["final_level"])

            name = get_species_name(pokemon["species_id"])
            return await ctx.send(
                f"Trained {name} for {plan['performed']} sessions. +{plan['total_xp']} XP, "
                f"+{plan['levels_gained']} levels. Cost: ${cost}. New level: {plan['final_level']}."
            )

        if action == "evolve":
            preview = self._preview_evolve(uid, pokemon, payload.get("target"))
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            return await self.pokemon_evolve(ctx, pokemon_id, payload.get("target"))

        if action == "heal":
            preview = self._preview_heal(uid, pokemon, payload.get("item_name"))
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            return await self.pokemon_heal(ctx, pokemon_id, payload.get("item_name"))

        if action == "revive":
            preview = self._preview_revive(uid, pokemon, payload.get("item_name"))
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            return await self.pokemon_revive(ctx, pokemon_id, payload.get("item_name"))

        if action == "reroll":
            preview = self._preview_reroll(uid, pokemon, payload.get("lock_stat"))
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            return await self.pokemon_reroll(ctx, pokemon_id, payload.get("lock_stat"))

        if action == "daycare_add":
            preview = self._preview_daycare(uid, pokemon, payload.get("hours", 0))
            if preview.get("error"):
                return await self._send_message(interaction, preview["error"], ephemeral=True)
            if preview.get("cost") != payload.get("cost"):
                return await self._send_message(interaction, "Cost changed. Please try again.", ephemeral=True)
            return await self.pokemon_daycare_add(ctx, pokemon_id, payload.get("hours", 0))

        return await self._send_message(interaction, "Action not supported yet.", ephemeral=True)

    async def handle_confirm_cancel(self, interaction, payload):
        mode = payload.get("return_mode", "dismiss")
        if mode == "dismiss":
            embed = discord.Embed(title="Cancelled", description="Action cancelled.", color=0x95a5a6)
            if interaction.response.is_done():
                await interaction.message.edit(embed=embed, view=None)
            else:
                await interaction.response.edit_message(embed=embed, view=None)
            return

        if mode == "zones":
            embed = self.build_zones_embed(str(interaction.user.id))
            view = PokemonMenuView(self, interaction.user.id, show_party_buttons=False)
        elif mode == "party":
            embed = self.build_party_embed(str(interaction.user.id))
            view = PokemonMenuView(self, interaction.user.id, show_party_buttons=True)
        else:
            embed = self.build_actions_embed(str(interaction.user.id))
            view = PokemonMenuView(self, interaction.user.id, show_party_buttons=False, menu="actions")

        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=view)
        else:
            await interaction.response.edit_message(embed=embed, view=view)
        view.message = interaction.message

    async def _resolve_member(self, guild, text):
        if not guild:
            return None
        value = (text or "").strip()
        if not value:
            return None
        match = re.match(r"<@!?(\d+)>", value)
        member_id = None
        if match:
            member_id = int(match.group(1))
        elif value.isdigit():
            member_id = int(value)
        else:
            lowered = value.lower()
            for member in guild.members:
                if member.display_name.lower() == lowered or member.name.lower() == lowered:
                    return member
            return None
        member = guild.get_member(member_id)
        if member:
            return member
        try:
            return await guild.fetch_member(member_id)
        except Exception:
            return None

    async def _send_party_overview(self, target):
        user = target.author if hasattr(target, "author") else target.user
        embed = self.build_party_embed(user.id)
        view = PokemonMenuView(self, user.id, show_party_buttons=True)
        message = await self._send_message(target, embed=embed, view=view)
        if message:
            view.message = message

    async def _start_wild_battle(self, target, encounter, owner_id, intro_line=None):
        equipped = db.get_equipped_pokemon(owner_id)
        if not equipped:
            return await self._send_message(target, "Equip a pokemon first with `!pokemon equip <id>`.", ephemeral=True)
        if equipped.get("is_fainted"):
            return await self._send_message(target, "Your equipped pokemon is fainted. Use `!pokemon revive`.", ephemeral=True)

        player = self._prepare_pokemon_state(owner_id, equipped)
        enemy = self._prepare_wild_state(encounter)

        if isinstance(target, discord.Interaction):
            ctx = SimpleNamespace(author=target.user)
        else:
            ctx = target

        view = WildBattleView(self, ctx, player, enemy, owner_id)
        log_lines = [intro_line] if intro_line else None
        embed = view.build_embed(log_lines)
        if isinstance(target, discord.Interaction):
            if target.response.is_done():
                message = await target.followup.send(embed=embed, view=view)
            else:
                await target.response.send_message(embed=embed, view=view)
                message = await target.original_response()
        else:
            message = await target.send(embed=embed, view=view)
        view.message = message

    def _resolve_catch(self, owner_id, ball_name):
        uid = str(owner_id)
        encounter = db.get_pokemon_encounter(uid)
        if not encounter:
            return {"status": "no_encounter", "message": "No wild pokemon found. Use `!pokemon hunt`."}

        now = datetime.now(timezone.utc).timestamp()
        if encounter["expires_at"] <= now:
            db.clear_pokemon_encounter(uid)
            return {"status": "expired", "message": "The wild pokemon fled. Hunt again."}
        if encounter["current_hp"] <= 0:
            db.clear_pokemon_encounter(uid)
            return {"status": "fainted", "message": "The wild pokemon fainted."}

        capacity = self._get_capacity(uid)
        owned_count = db.count_owned_pokemon(uid)
        if owned_count >= capacity:
            return {
                "status": "full",
                "message": f"You already have {capacity} pokemon. Release one first."
            }

        item_id, item = self.find_item(ball_name)
        if not item or item.get("type") != "pokemon_ball":
            return {"status": "invalid_ball", "message": "Invalid ball. Use a pokemon ball from the shop."}

        qty = db.get_inventory_item(uid, item_id)
        if qty <= 0:
            return {"status": "no_item", "message": f"You do not have any {item.get('name', item_id)}."}

        db.update_inventory(uid, item_id, -1)

        species = get_species(encounter["species_id"]) or {}
        base_rate = float(species.get("catch_rate", 0.35))
        ball_bonus = float(item.get("catch_bonus", 0.0))
        hp_ratio = encounter["current_hp"] / max(1, encounter["max_hp"])
        hp_bonus = (1 - hp_ratio) * 0.4
        chance = max(0.05, min(0.95, base_rate + ball_bonus + hp_bonus))
        caught = random.random() < chance

        if caught:
            ivs = roll_ivs()
            trait = roll_trait()
            stats = calc_stats(species, encounter["level"], ivs, trait)
            pokemon_id = uuid4().hex[:8]
            had_equipped = db.get_equipped_pokemon(uid)
            db.add_pokemon(
                pokemon_id=pokemon_id,
                owner_id=uid,
                species_id=encounter["species_id"],
                rarity=encounter["rarity"],
                level=encounter["level"],
                xp=0,
                ivs=ivs,
                trait=trait,
                current_hp=stats["hp"],
                is_equipped=False,
            )
            equipped_note = ""
            if not had_equipped:
                db.set_equipped_pokemon(uid, pokemon_id)
                equipped_note = " It has been equipped."
            db.clear_pokemon_encounter(uid)
            db.record_pokedex_catch(uid, encounter["species_id"])
            self._update_weekly(uid, "catches", 1)
            name = species.get("name", encounter["species_id"])
            return {
                "status": "caught",
                "message": f"You caught {name} (id:{pokemon_id}).{equipped_note}"
            }

        attempts = int(encounter.get("attempts", 0)) + 1
        if attempts >= MAX_CATCH_ATTEMPTS:
            db.clear_pokemon_encounter(uid)
            name = species.get("name", encounter["species_id"])
            return {"status": "fled", "message": f"{name} broke free and fled."}

        db.update_pokemon_encounter_attempts(uid, attempts)
        return {
            "status": "escaped",
            "message": f"The pokemon escaped. Attempts: {attempts}/{MAX_CATCH_ATTEMPTS}.",
            "attempts": attempts,
        }

    def resolve_attack(self, attacker, defender, move):
        name = get_species_name(attacker["species_id"])
        move_name = move.get("name", "Move")
        dmg = calc_damage(
            attacker["stats"],
            defender["stats"],
            move,
            attacker["types"],
            defender["types"],
            attacker["level"],
        )
        if dmg <= 0:
            return 0, f"{name} used {move_name} but missed."
        multiplier = type_multiplier(move.get("type"), defender["types"])
        effect_note = ""
        if multiplier >= 1.5:
            effect_note = " It's super effective!"
        elif multiplier <= 0.75:
            effect_note = " It's not very effective."
        return dmg, f"{name} used {move_name} and dealt {dmg} damage.{effect_note}"

    def apply_xp_gain(self, owner_id, pokemon_id, level, xp, gain):
        new_level, new_xp, levels_gained = apply_xp(level, xp, gain)
        owner_id_str = str(owner_id)
        pokemon = db.get_pokemon_by_id(owner_id_str, pokemon_id)
        db.update_pokemon_progress(owner_id_str, pokemon_id, new_level, new_xp)
        if levels_gained > 0:
            self._update_weekly(owner_id_str, "levels", levels_gained)
            if pokemon:
                self._apply_hp_delta(owner_id_str, pokemon, new_level=new_level)
        return new_level, new_xp, levels_gained

    @commands.group(name="pokemon", aliases=["poke"], invoke_without_command=True)
    async def pokemon(self, ctx):
        """View your pokemon."""
        await self._send_party_overview(ctx)

    @pokemon.command(name="starter")
    @economy_allowed()
    async def pokemon_starter(self, ctx, species_id: str = None):
        """Choose your starter pokemon."""
        uid = str(ctx.author.id)
        if db.count_owned_pokemon(uid) > 0:
            return await ctx.send("You already have pokemon. Use `!pokemon hunt` to find more.")

        if species_id is None:
            options = ", ".join(STARTER_SPECIES)
            return await ctx.send(f"Choose your starter: {options}\nUse `!pokemon starter <name>`.")

        species_id = species_id.lower().strip()
        if species_id not in STARTER_SPECIES:
            options = ", ".join(STARTER_SPECIES)
            return await ctx.send(f"Invalid starter. Choose one of: {options}")

        species = get_species(species_id)
        if not species:
            return await ctx.send("Starter data missing. Contact admin.")

        ivs = roll_ivs()
        trait = roll_trait()
        stats = calc_stats(species, 1, ivs, trait)
        pokemon_id = uuid4().hex[:8]
        db.add_pokemon(
            pokemon_id=pokemon_id,
            owner_id=uid,
            species_id=species_id,
            rarity=species.get("rarity", "common"),
            level=1,
            xp=0,
            ivs=ivs,
            trait=trait,
            current_hp=stats["hp"],
            is_equipped=True,
        )
        db.set_equipped_pokemon(uid, pokemon_id)
        await ctx.send(f"You received {species.get('name', species_id)} (id:{pokemon_id}) and equipped it.")

    @pokemon.command(name="list", aliases=["party"])
    async def pokemon_list(self, ctx):
        """List your pokemon."""
        await self._send_party_overview(ctx)

    @pokemon.command(name="equip")
    async def pokemon_equip(self, ctx, pokemon_id: str):
        """Equip one pokemon to gain bonuses."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        db.set_equipped_pokemon(uid, pokemon_id)
        name = get_species_name(pokemon["species_id"])
        await ctx.send(f"Equipped {name} (id:{pokemon_id}).")

    @pokemon.command(name="info")
    async def pokemon_info(self, ctx, pokemon_id: str):
        """Show pokemon details."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        embed = self.build_info_embed(uid, pokemon)
        view = PokemonMenuView(self, ctx.author.id)
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="help")
    async def pokemon_help(self, ctx):
        """Show pokemon commands and tips."""
        embed = self.build_help_embed()
        view = PokemonMenuView(self, ctx.author.id)
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="pokedex")
    async def pokemon_pokedex(self, ctx):
        """Show pokedex milestones."""
        uid = str(ctx.author.id)
        embed = self.build_pokedex_embed(uid)
        view = PokemonMenuView(self, ctx.author.id)
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="zones")
    async def pokemon_zones(self, ctx):
        """List hunt zones and unlocks."""
        uid = str(ctx.author.id)
        embed = self.build_zones_embed(uid)
        view = PokemonMenuView(self, ctx.author.id)
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="hunt")
    @economy_allowed()
    async def pokemon_hunt(self, ctx, zone_id: str = None):
        """Spend money to find a wild pokemon."""
        uid = str(ctx.author.id)
        result = self._prepare_hunt(uid, zone_id)
        if result.get("error"):
            return await ctx.send(result["error"])
        await self._start_wild_battle(ctx, result["encounter"], uid, intro_line=result["intro"])

    @pokemon.command(name="battle")
    async def pokemon_battle(self, ctx):
        """Battle the current wild pokemon."""
        uid = str(ctx.author.id)
        encounter = db.get_pokemon_encounter(uid)
        if not encounter:
            return await ctx.send("No wild pokemon found. Use `!pokemon hunt`.")
        if encounter["current_hp"] <= 0:
            return await ctx.send("The wild pokemon has fainted.")
        await self._start_wild_battle(ctx, encounter, uid)

    @pokemon.command(name="catch")
    async def pokemon_catch(self, ctx, ball_name: str = "pokeball"):
        """Catch the current wild pokemon."""
        uid = str(ctx.author.id)
        result = self._resolve_catch(uid, ball_name)
        await ctx.send(result["message"])

    @pokemon.command(name="daily")
    @economy_allowed()
    async def pokemon_daily(self, ctx):
        """Collect daily income from your equipped pokemon."""
        uid = str(ctx.author.id)
        result = self._claim_daily(uid)
        view = PokemonMenuView(self, ctx.author.id)
        if result.get("error"):
            return await ctx.send(result["error"])
        if result.get("cooldown"):
            embed = self.build_daily_cooldown_embed(result["cooldown"])
            message = await ctx.send(embed=embed, view=view)
            view.message = message
            return
        embed = self.build_daily_embed(result["name"], result["income"])
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="train")
    @economy_allowed()
    async def pokemon_train(self, ctx, pokemon_id: str, sessions: int = 1):
        """Train a pokemon by spending money to gain XP."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")

        if pokemon.get("is_fainted"):
            return await ctx.send("That pokemon is fainted. Revive it first.")

        if sessions <= 0:
            return await ctx.send("Sessions must be positive.")
        sessions = min(sessions, 25)

        wallet = self.economy_manager.get_balance(uid, "wallet")
        total_cost = 0
        total_xp = 0
        levels_gained = 0
        performed = 0
        level = pokemon["level"]
        xp = pokemon["xp"]
        rarity = pokemon["rarity"]

        for _ in range(sessions):
            cost = calc_train_cost(level, rarity)
            cost = self._apply_discount(cost, uid)
            if wallet < total_cost + cost:
                break
            gain = random.randint(TRAIN_XP_MIN, TRAIN_XP_MAX)
            level, xp, gained = apply_xp(level, xp, gain)
            total_cost += cost
            total_xp += gain
            levels_gained += gained
            performed += 1

        if total_cost == 0:
            return await ctx.send("Not enough money to train.")

        self.economy_manager.update_balance(uid, -total_cost, "wallet")
        db.update_pokemon_progress(uid, pokemon_id, level, xp)
        if levels_gained > 0:
            self._update_weekly(uid, "levels", levels_gained)
            self._apply_hp_delta(uid, pokemon, new_level=level)

        name = get_species_name(pokemon["species_id"])
        await ctx.send(
            f"Trained {name} for {performed} sessions. +{total_xp} XP, "
            f"+{levels_gained} levels. Cost: ${total_cost}. New level: {level}."
        )

    @pokemon.command(name="evolve")
    @economy_allowed()
    async def pokemon_evolve(self, ctx, pokemon_id: str, target: str = None):
        """Evolve a pokemon if eligible."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")

        species = get_species(pokemon["species_id"]) or {}
        evolves_to = species.get("evolves_to")
        evolve_level = int(species.get("evolve_level", 0) or 0)
        evolve_item = species.get("evolve_item")
        evolve_options = species.get("evolve_options", [])

        if evolve_options:
            if not target:
                options = ", ".join([f"{opt['species_id']} ({opt['item_id']})" for opt in evolve_options])
                return await ctx.send(f"Choose evolution: {options}\nUse `!pokemon evolve {pokemon_id} <species_id>`.")
            target = target.lower().strip()
            chosen = None
            for opt in evolve_options:
                if opt.get("species_id") == target:
                    chosen = opt
                    break
            if not chosen:
                return await ctx.send("Invalid evolution choice.")
            item_id = chosen.get("item_id")
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                return await ctx.send(f"You need {item_id} to evolve.")
            db.update_inventory(uid, item_id, -1)
            new_species_id = chosen.get("species_id")
        elif evolve_item:
            qty = db.get_inventory_item(uid, evolve_item)
            if qty <= 0:
                return await ctx.send(f"You need {evolve_item} to evolve.")
            db.update_inventory(uid, evolve_item, -1)
            new_species_id = evolves_to
        else:
            if not evolves_to:
                return await ctx.send("This pokemon cannot evolve.")
            if pokemon["level"] < evolve_level:
                return await ctx.send(f"Need level {evolve_level} to evolve.")
            new_species_id = evolves_to

        new_species = get_species(new_species_id) or {}
        new_rarity = new_species.get("rarity", pokemon["rarity"])
        cost = int(EVOLVE_BASE_COST * get_rarity_multiplier(new_rarity))
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return await ctx.send(f"You need ${cost} to evolve.")

        self.economy_manager.update_balance(uid, -cost, "wallet")
        db.update_pokemon_species(uid, pokemon_id, new_species_id, new_rarity)
        self._apply_hp_delta(uid, pokemon, new_species_id=new_species_id)

        old_name = get_species_name(pokemon["species_id"])
        new_name = get_species_name(new_species_id)
        await ctx.send(f"{old_name} evolved into {new_name}. Cost: ${cost}.")

    @pokemon.command(name="heal")
    @economy_allowed()
    async def pokemon_heal(self, ctx, pokemon_id: str, item_name: str = None):
        """Heal a pokemon."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        if pokemon.get("is_fainted"):
            return await ctx.send("That pokemon is fainted. Use `!pokemon revive`.")

        state = self._prepare_pokemon_state(uid, pokemon)
        missing = state["max_hp"] - state["current_hp"]
        if missing <= 0:
            return await ctx.send("That pokemon is already at full HP.")

        if item_name:
            item_id, item = self.find_item(item_name)
            if not item or item.get("type") != "pokemon_heal":
                return await ctx.send("Invalid heal item.")
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                return await ctx.send(f"You do not have any {item.get('name', item_id)}.")
            heal_amount = int(item.get("heal_amount", 0))
            if heal_amount <= 0:
                return await ctx.send("This item cannot heal.")
            db.update_inventory(uid, item_id, -1)
            new_hp = min(state["max_hp"], state["current_hp"] + heal_amount)
            db.update_pokemon_hp(uid, pokemon_id, new_hp)
            return await ctx.send(f"Healed to {new_hp}/{state['max_hp']} HP using {item.get('name', item_id)}.")

        cost = calc_heal_cost(missing, pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return await ctx.send(f"You need ${cost} to heal.")
        self.economy_manager.update_balance(uid, -cost, "wallet")
        db.update_pokemon_hp(uid, pokemon_id, state["max_hp"])
        await ctx.send(f"Healed to full HP. Cost: ${cost}.")

    @pokemon.command(name="revive")
    @economy_allowed()
    async def pokemon_revive(self, ctx, pokemon_id: str, item_name: str = None):
        """Revive a fainted pokemon."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        if not pokemon.get("is_fainted"):
            return await ctx.send("That pokemon is not fainted.")

        state = self._prepare_pokemon_state(uid, pokemon)
        if item_name:
            item_id, item = self.find_item(item_name)
            if not item or item.get("type") != "pokemon_revive":
                return await ctx.send("Invalid revive item.")
            qty = db.get_inventory_item(uid, item_id)
            if qty <= 0:
                return await ctx.send(f"You do not have any {item.get('name', item_id)}.")
            revive_pct = float(item.get("revive_percent", 0.0))
            if revive_pct <= 0:
                return await ctx.send("This item cannot revive.")
            db.update_inventory(uid, item_id, -1)
            new_hp = max(1, int(state["max_hp"] * revive_pct))
            db.update_pokemon_hp(uid, pokemon_id, new_hp)
            return await ctx.send(f"Revived to {new_hp}/{state['max_hp']} HP using {item.get('name', item_id)}.")

        cost = calc_revive_cost(pokemon["level"], pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return await ctx.send(f"You need ${cost} to revive.")
        self.economy_manager.update_balance(uid, -cost, "wallet")
        new_hp = max(1, int(state["max_hp"] * 0.5))
        db.update_pokemon_hp(uid, pokemon_id, new_hp)
        await ctx.send(f"Revived to {new_hp}/{state['max_hp']} HP. Cost: ${cost}.")

    @pokemon.command(name="reroll")
    @economy_allowed()
    async def pokemon_reroll(self, ctx, pokemon_id: str, lock_stat: str = None):
        """Reroll IVs and trait. Optionally lock one stat."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")

        lock_stat = lock_stat.lower().strip() if lock_stat else None
        if lock_stat and lock_stat not in {"hp", "atk", "def", "spd"}:
            return await ctx.send("Lock stat must be one of: hp, atk, def, spd.")

        cost = calc_reroll_cost(pokemon["level"], pokemon["rarity"], locked=bool(lock_stat))
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return await ctx.send(f"You need ${cost} to reroll.")

        new_ivs = roll_ivs()
        if lock_stat:
            new_ivs[lock_stat] = pokemon.get("ivs", {}).get(lock_stat, new_ivs[lock_stat])
        new_trait = roll_trait()
        db.update_pokemon_ivs_trait(uid, pokemon_id, new_ivs, new_trait)
        self.economy_manager.update_balance(uid, -cost, "wallet")
        await ctx.send(f"Rerolled IVs and trait. Cost: ${cost}.")

    @pokemon.group(name="daycare", invoke_without_command=True)
    async def pokemon_daycare(self, ctx):
        """View daycare status."""
        await self.pokemon_daycare_status(ctx)

    @pokemon_daycare.command(name="add")
    async def pokemon_daycare_add(self, ctx, pokemon_id: str, hours: int):
        """Send a pokemon to daycare."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        if pokemon.get("is_fainted"):
            return await ctx.send("That pokemon is fainted.")
        if hours <= 0 or hours > 24:
            return await ctx.send("Hours must be between 1 and 24.")

        existing = db.get_daycare_entries(uid)
        if any(entry["pokemon_id"] == pokemon_id for entry in existing):
            return await ctx.send("That pokemon is already in daycare.")

        cost = calc_daycare_cost(hours, pokemon["rarity"])
        cost = self._apply_discount(cost, uid)
        wallet = self.economy_manager.get_balance(uid, "wallet")
        if wallet < cost:
            return await ctx.send(f"You need ${cost} to use daycare.")

        start_at = datetime.now(timezone.utc).timestamp()
        end_at = start_at + (hours * 3600)
        xp_per_hour = calc_daycare_xp(1, pokemon["rarity"])
        db.add_daycare_entry(pokemon_id, uid, start_at, end_at, xp_per_hour, cost)
        self.economy_manager.update_balance(uid, -cost, "wallet")
        await ctx.send(f"Daycare started for {hours}h. Cost: ${cost}.")

    @pokemon_daycare.command(name="status")
    async def pokemon_daycare_status(self, ctx):
        """Show daycare status."""
        uid = str(ctx.author.id)
        entries = db.get_daycare_entries(uid)
        if not entries:
            return await ctx.send("No pokemon in daycare.")
        now = datetime.now(timezone.utc).timestamp()
        lines = []
        for entry in entries:
            pokemon = db.get_pokemon_by_id(uid, entry["pokemon_id"])
            name = get_species_name(pokemon["species_id"]) if pokemon else entry["pokemon_id"]
            remaining = max(0, int(entry["end_at"] - now))
            h, rem = divmod(remaining, 3600)
            m, _ = divmod(rem, 60)
            lines.append(f"{name} (id:{entry['pokemon_id']}): {h}h {m}m remaining")
        await ctx.send("\n".join(lines))

    @pokemon_daycare.command(name="claim")
    async def pokemon_daycare_claim(self, ctx):
        """Claim daycare XP."""
        uid = str(ctx.author.id)
        entries = db.get_daycare_entries(uid)
        if not entries:
            return await ctx.send("No pokemon in daycare.")
        now = datetime.now(timezone.utc).timestamp()
        claimed = []
        pending = []
        for entry in entries:
            if entry["end_at"] > now:
                pending.append(entry)
                continue
            pokemon = db.get_pokemon_by_id(uid, entry["pokemon_id"])
            if not pokemon:
                db.remove_daycare_entry(entry["pokemon_id"])
                continue
            hours = int((entry["end_at"] - entry["start_at"]) / 3600)
            xp_gain = entry["xp_per_hour"] * max(1, hours)
            new_level, new_xp, levels_gained = self.apply_xp_gain(uid, pokemon["pokemon_id"], pokemon["level"], pokemon["xp"], xp_gain)
            claimed.append(f"{get_species_name(pokemon['species_id'])}: +{xp_gain} XP")
            if levels_gained > 0:
                claimed.append(f"Level up! +{levels_gained} levels.")
            db.remove_daycare_entry(entry["pokemon_id"])
        if claimed:
            await ctx.send("\n".join(claimed))
        if pending:
            await ctx.send("Some pokemon are still training. Use `!pokemon daycare status`.")

    @pokemon.command(name="weekly")
    async def pokemon_weekly(self, ctx):
        """Show weekly mission progress."""
        uid = str(ctx.author.id)
        embed = self.build_weekly_embed(uid)
        view = PokemonMenuView(self, ctx.author.id)
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="weekly_claim")
    async def pokemon_weekly_claim(self, ctx):
        """Claim weekly mission reward."""
        uid = str(ctx.author.id)
        data = self._get_weekly(uid)
        if data["reward_claimed"]:
            return await ctx.send("Weekly reward already claimed.")
        if (
            data["hunts"] < WEEKLY_TARGETS["hunts"]
            or data["catches"] < WEEKLY_TARGETS["catches"]
            or data["levels"] < WEEKLY_TARGETS["levels"]
        ):
            return await ctx.send("Weekly missions not completed yet.")

        for item_id, qty in WEEKLY_REWARD_ITEMS.items():
            db.update_inventory(uid, item_id, qty)
        db.set_active_buff(uid, "fee_discount", WEEKLY_REWARD_DISCOUNT, WEEKLY_REWARD_DISCOUNT_MINUTES * 60)
        db.set_weekly(uid, data["week_start"], data["hunts"], data["catches"], data["levels"], True)
        await ctx.send("Weekly reward claimed! You received balls and a fee discount buff.")

    @pokemon.command(name="duel")
    @economy_allowed()
    async def pokemon_duel(self, ctx, opponent: discord.Member):
        """Challenge another user to a pokemon duel."""
        await self._start_duel(ctx, opponent)

    async def _start_duel(self, ctx, opponent: discord.Member):
        if opponent.bot or opponent == ctx.author:
            return await ctx.send("Invalid opponent.")

        player_pokemon = db.get_equipped_pokemon(str(ctx.author.id))
        opp_pokemon = db.get_equipped_pokemon(str(opponent.id))
        if not player_pokemon:
            return await ctx.send("Equip a pokemon first with `!pokemon equip <id>`.")
        if not opp_pokemon:
            return await ctx.send(f"{opponent.display_name} has no equipped pokemon.")
        if player_pokemon.get("is_fainted"):
            return await ctx.send("Your equipped pokemon is fainted.")
        if opp_pokemon.get("is_fainted"):
            return await ctx.send(f"{opponent.display_name}'s pokemon is fainted.")

        player_state = self._prepare_pokemon_state(ctx.author.id, player_pokemon)
        opp_state = self._prepare_pokemon_state(opponent.id, opp_pokemon)
        player_state["owner_name"] = ctx.author.display_name
        opp_state["owner_name"] = opponent.display_name

        view = DuelBattleView(self, ctx, player_state, opp_state)
        embed = view.build_embed()
        message = await ctx.send(embed=embed, view=view)
        view.message = message

    @pokemon.command(name="upgrade")
    @economy_allowed()
    async def pokemon_upgrade(self, ctx, item_name: str = "pc_upgrade"):
        """Use an item to increase pokemon capacity."""
        uid = str(ctx.author.id)
        item_id, item = self.find_item(item_name)
        if not item or item.get("type") != "pokemon_capacity":
            return await ctx.send("Invalid capacity item.")
        qty = db.get_inventory_item(uid, item_id)
        if qty <= 0:
            return await ctx.send(f"You do not have any {item.get('name', item_id)}.")
        slots = int(item.get("slot_increase", 1))
        db.update_inventory(uid, item_id, -1)
        db.add_pokemon_capacity_bonus(uid, slots)
        new_limit = self._get_capacity(uid)
        await ctx.send(f"Pokemon capacity increased by {slots}. New limit: {new_limit}.")

    @pokemon.command(name="release")
    async def pokemon_release(self, ctx, pokemon_id: str):
        """Release a pokemon."""
        uid = str(ctx.author.id)
        pokemon = db.get_pokemon_by_id(uid, pokemon_id)
        if not pokemon:
            return await ctx.send("Pokemon not found.")
        if pokemon["is_equipped"]:
            db.clear_equipped_pokemon(uid)
        db.delete_pokemon(uid, pokemon_id)
        name = get_species_name(pokemon["species_id"])
        await ctx.send(f"Released {name} (id:{pokemon_id}).")


async def setup(bot):
    await bot.add_cog(Pokemon(bot))
