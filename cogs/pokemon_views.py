import discord
import random
import logging

from utils.database import db
from utils.pokemon_system import (
    calc_battle_xp,
    get_species_name,
    calc_run_chance,
    get_species,
    calc_stats,
)

logger = logging.getLogger("bot")


def render_hp_bar(current_hp, max_hp, length=18):
    if max_hp <= 0:
        return "[----------]"
    ratio = max(0.0, min(1.0, current_hp / max_hp))
    filled = int(ratio * length)
    return "[" + ("#" * filled) + ("-" * (length - filled)) + "]"


class PokemonMoveButton(discord.ui.Button):
    def __init__(self, move, row=0):
        label = move.get("name", "Move")
        super().__init__(label=label, style=discord.ButtonStyle.primary, row=row)
        self.move = move

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_move(interaction, self.move)


class PokemonBallSelect(discord.ui.Select):
    def __init__(self, options):
        disabled = False
        if not options:
            options = [discord.SelectOption(label="No balls available", value="none")]
            disabled = True
        super().__init__(
            placeholder="🎯 Throw a ball...",
            options=options,
            min_values=1,
            max_values=1,
            row=2,
        )
        self.disabled = disabled

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_catch(interaction, self.values[0])


class PokemonRunButton(discord.ui.Button):
    def __init__(self):
        super().__init__(label="🏃 Run", style=discord.ButtonStyle.danger, row=3)

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_run(interaction)


class PokemonMenuButton(discord.ui.Button):
    def __init__(self, label, action, style=discord.ButtonStyle.secondary, row=0):
        super().__init__(label=label, style=style, row=row)
        self.action = action

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_action(interaction, self.action)


class PokemonInfoButton(discord.ui.Button):
    def __init__(self, pokemon_id, label, row=3):
        super().__init__(label=label, style=discord.ButtonStyle.secondary, row=row)
        self.pokemon_id = pokemon_id

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_party_info(interaction, self.pokemon_id)


class PokemonInfoSelect(discord.ui.Select):
    def __init__(self, options, row=4, placeholder="🔍 View Pokemon info..."):
        disabled = False
        if not options:
            options = [discord.SelectOption(label="No pokemon available", value="none")]
            disabled = True
        super().__init__(
            placeholder=placeholder,
            options=options,
            min_values=1,
            max_values=1,
            row=row,
        )
        self.disabled = disabled

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_party_info_select(interaction, self.values[0])


class PokemonZoneSelect(discord.ui.Select):
    def __init__(self, options, row=2):
        disabled = False
        if not options:
            options = [discord.SelectOption(label="No zones available", value="none")]
            disabled = True
        super().__init__(
            placeholder="🧭 Select a zone to hunt...",
            options=options,
            min_values=1,
            max_values=1,
            row=row,
        )
        self.disabled = disabled

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_hunt(interaction, self.values[0])


class PokemonExtraModal(discord.ui.Modal):
    def __init__(
        self,
        pokemon_cog,
        action,
        pokemon_id,
        origin,
        page,
        title,
        extra_label,
        extra_placeholder,
        extra_required=False,
    ):
        super().__init__(title=title)
        self.pokemon_cog = pokemon_cog
        self.action = action
        self.pokemon_id = pokemon_id
        self.origin = origin
        self.page = page
        self.extra = discord.ui.TextInput(
            label=extra_label,
            placeholder=extra_placeholder,
            max_length=64,
            required=extra_required,
        )
        self.add_item(self.extra)

    async def on_submit(self, interaction: discord.Interaction):
        await self.pokemon_cog.handle_modal_action(
            interaction,
            self.action,
            self.pokemon_id,
            str(self.extra.value).strip(),
            origin=self.origin,
            page=self.page,
        )


class PokemonTextModal(discord.ui.Modal):
    def __init__(
        self,
        pokemon_cog,
        action,
        title,
        label,
        placeholder,
        required=True,
    ):
        super().__init__(title=title)
        self.pokemon_cog = pokemon_cog
        self.action = action
        self.value_input = discord.ui.TextInput(
            label=label,
            placeholder=placeholder,
            max_length=64,
            required=required,
        )
        self.add_item(self.value_input)

    async def on_submit(self, interaction: discord.Interaction):
        await self.pokemon_cog.handle_text_action(
            interaction,
            self.action,
            str(self.value_input.value).strip(),
        )


class PokemonActionSelect(discord.ui.Select):
    def __init__(self, options, row=0):
        disabled = False
        if not options:
            options = [discord.SelectOption(label="No actions available", value="none")]
            disabled = True
        super().__init__(
            placeholder="🧰 Choose an action...",
            options=options,
            min_values=1,
            max_values=1,
            row=row,
        )
        self.disabled = disabled

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_action_select(interaction, self.values[0])


class PokemonPickSelect(discord.ui.Select):
    def __init__(self, options, placeholder="Select a pokemon...", row=0):
        disabled = False
        if not options:
            options = [discord.SelectOption(label="No pokemon available", value="none")]
            disabled = True
        super().__init__(
            placeholder=placeholder,
            options=options,
            min_values=1,
            max_values=1,
            row=row,
        )
        self.disabled = disabled

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_pokemon_pick(interaction, self.values[0])


class PokemonMenuView(discord.ui.View):
    def __init__(self, pokemon_cog, owner_id, show_party_buttons=False, menu="main", party_page=0):
        super().__init__(timeout=180)
        self.pokemon_cog = pokemon_cog
        self.owner_id = int(owner_id)
        self.show_party_buttons = show_party_buttons
        self.menu = menu
        self.party_page = party_page

        if menu == "actions":
            action_options = self.pokemon_cog.build_action_options(self.owner_id)
            self.add_item(PokemonActionSelect(action_options, row=0))
            self.add_item(PokemonMenuButton("⬅️ Menu", "back", row=1))
        else:
            self.add_item(PokemonMenuButton("🎒 Party", "party", row=0))
            self.add_item(PokemonMenuButton("🧭 Hunt", "hunt", style=discord.ButtonStyle.success, row=0))
            self.add_item(PokemonMenuButton("🗺️ Zones", "zones", row=0))
            self.add_item(PokemonMenuButton("🎯 Daily", "daily", style=discord.ButtonStyle.primary, row=0))
            self.add_item(PokemonMenuButton("📘 Pokedex", "pokedex", row=0))

            self.add_item(PokemonMenuButton("📅 Weekly", "weekly", row=1))
            self.add_item(PokemonMenuButton("🆘 Help", "help", row=1))
            self.add_item(PokemonMenuButton("🔍 Info", "info", row=1))
            self.add_item(PokemonMenuButton("🧰 Actions", "actions", row=1))
            self.add_item(PokemonMenuButton("⭐ Equip", "equip", row=1))

            zone_options = self.pokemon_cog.build_zone_options(self.owner_id)
            self.add_item(PokemonZoneSelect(zone_options, row=2))

            if show_party_buttons:
                self._add_party_controls()

    async def interaction_check(self, interaction: discord.Interaction):
        if interaction.user.id != self.owner_id:
            await interaction.response.send_message("This menu isn't for you.", ephemeral=True)
            return False
        return True

    def _disable_view(self):
        for child in self.children:
            child.disabled = True

    async def on_timeout(self):
        self._disable_view()
        try:
            await self.message.edit(view=self)
        except Exception:
            pass

    def _add_party_controls(self):
        owned = db.get_owned_pokemon(str(self.owner_id))
        if not owned:
            self.add_item(PokemonInfoButton("none", "No party", row=3))
            self.children[-1].disabled = True
            self.add_item(PokemonInfoSelect([], row=4))
            return

        owned_sorted = sorted(owned, key=lambda p: (not p.get("is_equipped"), p.get("created_at", 0)))
        quick = owned_sorted[:5]
        for idx, pokemon in enumerate(quick):
            species = get_species_name(pokemon["species_id"])
            label = f"🔍 {species}"
            if len(label) > 24:
                label = label[:21] + "..."
            self.add_item(PokemonInfoButton(pokemon["pokemon_id"], label, row=3))

        options, page, total_pages, total_count = self.pokemon_cog.build_pokemon_select_options(
            self.owner_id,
            page=self.party_page,
        )
        placeholder = "🔍 View Pokemon info..."
        if total_pages > 1:
            placeholder = f"🔍 View Pokemon info (Page {page + 1}/{total_pages})"
        self.add_item(PokemonInfoSelect(options, row=4, placeholder=placeholder))

    async def handle_action(self, interaction: discord.Interaction, action: str):
        if action in {"party", "refresh"}:
            embed = self.pokemon_cog.build_party_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=True)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "back":
            embed = self.pokemon_cog.build_party_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=True)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "zones":
            embed = self.pokemon_cog.build_zones_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "pokedex":
            embed = self.pokemon_cog.build_pokedex_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "weekly":
            embed = self.pokemon_cog.build_weekly_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "help":
            embed = self.pokemon_cog.build_help_embed()
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False)
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "actions":
            embed = self.pokemon_cog.build_actions_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False, menu="actions")
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if action == "daily":
            return await self.pokemon_cog.handle_daily_interaction(interaction)
        if action == "hunt":
            return await self.pokemon_cog.handle_hunt_interaction(interaction, None, origin="main")
        if action in {"info", "equip"}:
            return await self.pokemon_cog.show_action_picker(interaction, action, origin="main")

    async def handle_hunt(self, interaction: discord.Interaction, zone_id: str):
        await self.pokemon_cog.handle_hunt_interaction(interaction, zone_id, origin="zones")

    async def handle_action_select(self, interaction: discord.Interaction, action: str):
        if action == "none":
            return await interaction.response.send_message("No actions available.", ephemeral=True)
        await self.pokemon_cog.handle_action_select(interaction, action)

    async def handle_party_info_select(self, interaction: discord.Interaction, pokemon_id: str):
        if pokemon_id == "__prev__":
            new_page = max(0, self.party_page - 1)
            embed = self.pokemon_cog.build_party_embed(str(self.owner_id))
            new_view = PokemonMenuView(
                self.pokemon_cog,
                self.owner_id,
                show_party_buttons=True,
                party_page=new_page,
            )
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        if pokemon_id == "__next__":
            new_page = self.party_page + 1
            embed = self.pokemon_cog.build_party_embed(str(self.owner_id))
            new_view = PokemonMenuView(
                self.pokemon_cog,
                self.owner_id,
                show_party_buttons=True,
                party_page=new_page,
            )
            await interaction.response.edit_message(embed=embed, view=new_view)
            new_view.message = interaction.message
            return
        await self.handle_party_info(interaction, pokemon_id)

    async def handle_party_info(self, interaction: discord.Interaction, pokemon_id: str):
        if pokemon_id == "none":
            return await interaction.response.send_message("No pokemon available.", ephemeral=True)
        pokemon = db.get_pokemon_by_id(str(self.owner_id), pokemon_id)
        if not pokemon:
            return await interaction.response.send_message("Pokemon not found.", ephemeral=True)
        embed = self.pokemon_cog.build_info_embed(str(self.owner_id), pokemon)
        await interaction.response.edit_message(embed=embed, view=self)


class PokemonActionPickerView(discord.ui.View):
    def __init__(self, pokemon_cog, owner_id, action, page=0, origin="actions"):
        super().__init__(timeout=180)
        self.pokemon_cog = pokemon_cog
        self.owner_id = int(owner_id)
        self.action = action
        self.page = page
        self.origin = origin

        options, page, total_pages, total_count = self.pokemon_cog.build_pokemon_select_options(
            self.owner_id,
            page=self.page,
        )
        self.page = page
        placeholder = "Select a pokemon..."
        if total_pages > 1:
            placeholder = f"Select a pokemon (Page {page + 1}/{total_pages})"
        self.add_item(PokemonPickSelect(options, placeholder=placeholder, row=0))
        self.add_item(PokemonMenuButton("⬅️ Back", "back", row=1))

    async def interaction_check(self, interaction: discord.Interaction):
        if interaction.user.id != self.owner_id:
            await interaction.response.send_message("This menu isn't for you.", ephemeral=True)
            return False
        return True

    def _disable_view(self):
        for child in self.children:
            child.disabled = True

    async def on_timeout(self):
        self._disable_view()
        try:
            await self.message.edit(view=self)
        except Exception:
            pass

    async def handle_action(self, interaction: discord.Interaction, action: str):
        if action != "back":
            return
        if self.origin == "actions":
            embed = self.pokemon_cog.build_actions_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=False, menu="actions")
        else:
            embed = self.pokemon_cog.build_party_embed(str(self.owner_id))
            new_view = PokemonMenuView(self.pokemon_cog, self.owner_id, show_party_buttons=True)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=new_view)
        else:
            await interaction.response.edit_message(embed=embed, view=new_view)
        new_view.message = interaction.message

    async def handle_pokemon_pick(self, interaction: discord.Interaction, pokemon_id: str):
        if pokemon_id == "none":
            return await interaction.response.send_message("No pokemon available.", ephemeral=True)
        if pokemon_id == "__prev__":
            return await self.pokemon_cog.show_action_picker(
                interaction,
                self.action,
                origin=self.origin,
                page=max(0, self.page - 1),
            )
        if pokemon_id == "__next__":
            return await self.pokemon_cog.show_action_picker(
                interaction,
                self.action,
                origin=self.origin,
                page=self.page + 1,
            )
        await self.pokemon_cog.handle_action_with_pokemon(
            interaction,
            self.action,
            pokemon_id,
            origin=self.origin,
            page=self.page,
        )


class PokemonConfirmButton(discord.ui.Button):
    def __init__(self):
        super().__init__(label="✅ Confirm", style=discord.ButtonStyle.success)

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_confirm(interaction)


class PokemonCancelButton(discord.ui.Button):
    def __init__(self):
        super().__init__(label="❌ Cancel", style=discord.ButtonStyle.secondary)

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_cancel(interaction)


class PokemonConfirmView(discord.ui.View):
    def __init__(self, pokemon_cog, owner_id, payload):
        super().__init__(timeout=120)
        self.pokemon_cog = pokemon_cog
        self.owner_id = int(owner_id)
        self.payload = payload

        self.add_item(PokemonConfirmButton())
        self.add_item(PokemonCancelButton())

    async def interaction_check(self, interaction: discord.Interaction):
        if interaction.user.id != self.owner_id:
            await interaction.response.send_message("This menu isn't for you.", ephemeral=True)
            return False
        return True

    def _disable_view(self):
        for child in self.children:
            child.disabled = True

    async def on_timeout(self):
        self._disable_view()
        try:
            await self.message.edit(view=self)
        except Exception:
            pass

    async def handle_confirm(self, interaction: discord.Interaction):
        self._disable_view()
        if interaction.response.is_done():
            await interaction.message.edit(view=self)
        else:
            await interaction.response.edit_message(view=self)
        await self.pokemon_cog.handle_cost_confirm(interaction, self.payload)

    async def handle_cancel(self, interaction: discord.Interaction):
        await self.pokemon_cog.handle_confirm_cancel(interaction, self.payload)


class WildBattleView(discord.ui.View):
    def __init__(self, pokemon_cog, ctx, player, enemy, encounter_owner_id):
        super().__init__(timeout=90)
        self.pokemon_cog = pokemon_cog
        self.ctx = ctx
        self.player = player
        self.enemy = enemy
        self.encounter_owner_id = encounter_owner_id
        self.resolved = False

        for idx, move in enumerate(player["moves"]):
            row = 0 if idx < 2 else 1
            self.add_item(PokemonMoveButton(move, row=row))

        ball_options = self.pokemon_cog.build_ball_options(self.encounter_owner_id)
        self.add_item(PokemonBallSelect(ball_options))
        self.add_item(PokemonRunButton())

    def _lock_view(self):
        self.resolved = True
        for child in self.children:
            child.disabled = True

    async def on_timeout(self):
        if self.resolved:
            return
        self._lock_view()
        try:
            await self.message.edit(content="Battle timed out.", view=self)
        except Exception:
            pass

    def build_embed(self, log_lines=None):
        log_lines = log_lines or []
        player_name = get_species_name(self.player["species_id"])
        enemy_name = get_species_name(self.enemy["species_id"])
        player_hp = f"{self.player['current_hp']}/{self.player['max_hp']}"
        enemy_hp = f"{self.enemy['current_hp']}/{self.enemy['max_hp']}"

        lines = [
            f"{player_name} (Lv {self.player['level']}) {render_hp_bar(self.player['current_hp'], self.player['max_hp'])} {player_hp}",
            f"{enemy_name} (Lv {self.enemy['level']}) {render_hp_bar(self.enemy['current_hp'], self.enemy['max_hp'])} {enemy_hp}",
        ]
        if log_lines:
            lines.append("")
            lines.extend(log_lines[-4:])
        return discord.Embed(title="Pokemon Battle", description="\n".join(lines), color=0x2ecc71)

    async def handle_move(self, interaction, move):
        if interaction.user != self.ctx.author:
            return await interaction.response.send_message("Not your battle.", ephemeral=True)
        if self.resolved:
            return

        log_lines = []
        enemy_move = random.choice(self.enemy["moves"])
        player_priority = int(move.get("priority", 0))
        enemy_priority = int(enemy_move.get("priority", 0))

        if enemy_priority > player_priority:
            order = [("enemy", enemy_move), ("player", move)]
        elif enemy_priority < player_priority:
            order = [("player", move), ("enemy", enemy_move)]
        else:
            if self.enemy["stats"]["spd"] > self.player["stats"]["spd"]:
                order = [("enemy", enemy_move), ("player", move)]
            elif self.enemy["stats"]["spd"] < self.player["stats"]["spd"]:
                order = [("player", move), ("enemy", enemy_move)]
            else:
                order = [("player", move), ("enemy", enemy_move)]
                if random.random() < 0.5:
                    order.reverse()

        for actor, chosen_move in order:
            if self.player["current_hp"] <= 0 or self.enemy["current_hp"] <= 0:
                break
            if actor == "player":
                dmg, text = self.pokemon_cog.resolve_attack(self.player, self.enemy, chosen_move)
                log_lines.append(text)
                self.enemy["current_hp"] = max(0, self.enemy["current_hp"] - dmg)
                if self.enemy["current_hp"] <= 0:
                    log_lines.append("Wild pokemon fainted.")
            else:
                dmg, text = self.pokemon_cog.resolve_attack(self.enemy, self.player, chosen_move)
                log_lines.append(text)
                self.player["current_hp"] = max(0, self.player["current_hp"] - dmg)
                if self.player["current_hp"] <= 0:
                    log_lines.append("Your pokemon fainted.")

        if self.player["current_hp"] <= 0 or self.enemy["current_hp"] <= 0:
            self._lock_view()

        db.update_pokemon_hp(str(self.ctx.author.id), self.player["pokemon_id"], self.player["current_hp"])
        if self.enemy["current_hp"] > 0:
            db.update_pokemon_encounter_hp(self.encounter_owner_id, self.enemy["current_hp"])
        else:
            db.clear_pokemon_encounter(self.encounter_owner_id)

        if self.resolved and self.enemy["current_hp"] <= 0:
            xp_gain = calc_battle_xp(self.enemy["level"], self.enemy["rarity"])
            result = self.pokemon_cog.apply_xp_gain(
                self.ctx.author.id,
                self.player["pokemon_id"],
                self.player["level"],
                self.player["xp"],
                xp_gain
            )
            if result:
                new_level, new_xp, levels_gained = result
                old_stats = self.player["stats"]
                self.player["level"] = new_level
                self.player["xp"] = new_xp
                if levels_gained > 0:
                    species = get_species(self.player["species_id"]) or {}
                    new_stats = calc_stats(species, new_level, self.player["ivs"], self.player["trait"])
                    delta_hp = max(0, new_stats["hp"] - old_stats["hp"])
                    self.player["stats"] = new_stats
                    self.player["max_hp"] = new_stats["hp"]
                    self.player["current_hp"] = min(new_stats["hp"], self.player["current_hp"] + delta_hp)
                if levels_gained > 0:
                    log_lines.append(f"Level up! +{levels_gained} levels.")

        embed = self.build_embed(log_lines)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=self)
        else:
            await interaction.response.edit_message(embed=embed, view=self)

    async def handle_catch(self, interaction, ball_id):
        if interaction.user != self.ctx.author:
            return await interaction.response.send_message("Not your battle.", ephemeral=True)
        if self.resolved:
            return

        result = self.pokemon_cog._resolve_catch(self.encounter_owner_id, ball_id)
        status = result.get("status")
        message = result.get("message", "...")

        if status in {"invalid_ball", "no_item", "full"}:
            return await interaction.response.send_message(message, ephemeral=True)

        log_lines = [message]

        if status in {"no_encounter", "expired", "fainted", "caught", "fled"}:
            self._lock_view()
        elif status == "escaped":
            enemy_move = random.choice(self.enemy["moves"])
            dmg, text = self.pokemon_cog.resolve_attack(self.enemy, self.player, enemy_move)
            log_lines.append(text)
            self.player["current_hp"] = max(0, self.player["current_hp"] - dmg)
            if self.player["current_hp"] <= 0:
                log_lines.append("Your pokemon fainted.")
                self._lock_view()

        db.update_pokemon_hp(str(self.ctx.author.id), self.player["pokemon_id"], self.player["current_hp"])

        embed = self.build_embed(log_lines)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=self)
        else:
            await interaction.response.edit_message(embed=embed, view=self)

    async def handle_run(self, interaction):
        if interaction.user != self.ctx.author:
            return await interaction.response.send_message("Not your battle.", ephemeral=True)
        if self.resolved:
            return

        encounter = db.get_pokemon_encounter(self.encounter_owner_id)
        if not encounter:
            self._lock_view()
            log_lines = ["No wild pokemon found."]
            embed = self.build_embed(log_lines)
            if interaction.response.is_done():
                return await interaction.message.edit(embed=embed, view=self)
            return await interaction.response.edit_message(embed=embed, view=self)

        attempts = int(encounter.get("attempts", 0))
        chance = calc_run_chance(self.player["stats"]["spd"], self.enemy["stats"]["spd"], attempts)
        log_lines = []

        if random.random() < chance:
            db.clear_pokemon_encounter(self.encounter_owner_id)
            log_lines.append("You ran away safely.")
            self._lock_view()
        else:
            log_lines.append("Couldn't escape!")
            enemy_move = random.choice(self.enemy["moves"])
            dmg, text = self.pokemon_cog.resolve_attack(self.enemy, self.player, enemy_move)
            log_lines.append(text)
            self.player["current_hp"] = max(0, self.player["current_hp"] - dmg)
            if self.player["current_hp"] <= 0:
                log_lines.append("Your pokemon fainted.")
                self._lock_view()

        db.update_pokemon_hp(str(self.ctx.author.id), self.player["pokemon_id"], self.player["current_hp"])

        embed = self.build_embed(log_lines)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=self)
        else:
            await interaction.response.edit_message(embed=embed, view=self)


class DuelMoveButton(discord.ui.Button):
    def __init__(self, index: int):
        super().__init__(label="Move", style=discord.ButtonStyle.primary)
        self.index = index

    async def callback(self, interaction: discord.Interaction):
        view = self.view
        await view.handle_move(interaction, self.index)


class DuelBattleView(discord.ui.View):
    def __init__(self, pokemon_cog, ctx, player_a, player_b):
        super().__init__(timeout=120)
        self.pokemon_cog = pokemon_cog
        self.ctx = ctx
        self.players = {player_a["owner_id"]: player_a, player_b["owner_id"]: player_b}
        self.turn = self._decide_turn(player_a, player_b)
        self.resolved = False

        for i in range(4):
            self.add_item(DuelMoveButton(i))
        self.update_buttons()

    def _decide_turn(self, player_a, player_b):
        if player_a["stats"]["spd"] > player_b["stats"]["spd"]:
            return player_a["owner_id"]
        if player_b["stats"]["spd"] > player_a["stats"]["spd"]:
            return player_b["owner_id"]
        return random.choice([player_a["owner_id"], player_b["owner_id"]])

    def update_buttons(self):
        moves = self.players[self.turn]["moves"]
        for idx, child in enumerate(self.children):
            if idx < len(moves):
                child.label = moves[idx].get("name", "Move")
                child.disabled = False
            else:
                child.label = "-"
                child.disabled = True

    async def on_timeout(self):
        if self.resolved:
            return
        self.resolved = True
        for child in self.children:
            child.disabled = True
        try:
            await self.message.edit(content="Duel timed out.", view=self)
        except Exception:
            pass

    def build_embed(self, log_lines=None):
        log_lines = log_lines or []
        player_ids = list(self.players.keys())
        left = self.players[player_ids[0]]
        right = self.players[player_ids[1]]
        left_name = get_species_name(left["species_id"])
        right_name = get_species_name(right["species_id"])
        left_hp = f"{left['current_hp']}/{left['max_hp']}"
        right_hp = f"{right['current_hp']}/{right['max_hp']}"
        lines = [
            f"{left['owner_name']}: {left_name} (Lv {left['level']}) {render_hp_bar(left['current_hp'], left['max_hp'])} {left_hp}",
            f"{right['owner_name']}: {right_name} (Lv {right['level']}) {render_hp_bar(right['current_hp'], right['max_hp'])} {right_hp}",
            "",
            f"Turn: <@{self.turn}>",
        ]
        if log_lines:
            lines.append("")
            lines.extend(log_lines[-4:])
        return discord.Embed(title="Pokemon Duel", description="\n".join(lines), color=0x3498db)

    async def handle_move(self, interaction, move_index):
        if self.resolved:
            return
        if interaction.user.id != self.turn:
            return await interaction.response.send_message("Not your turn.", ephemeral=True)

        actor = self.players[self.turn]
        opponent_id = [pid for pid in self.players if pid != self.turn][0]
        opponent = self.players[opponent_id]
        moves = actor["moves"]
        if move_index >= len(moves):
            return await interaction.response.send_message("Invalid move.", ephemeral=True)

        move = moves[move_index]
        log_lines = []
        dmg, text = self.pokemon_cog.resolve_attack(actor, opponent, move)
        log_lines.append(text)
        opponent["current_hp"] = max(0, opponent["current_hp"] - dmg)

        if opponent["current_hp"] <= 0:
            self.resolved = True
            for child in self.children:
                child.disabled = True
            log_lines.append(f"{opponent['owner_name']}'s pokemon fainted.")
        else:
            self.turn = opponent_id
            self.update_buttons()

        db.update_pokemon_hp(str(actor["owner_id"]), actor["pokemon_id"], actor["current_hp"])
        db.update_pokemon_hp(str(opponent["owner_id"]), opponent["pokemon_id"], opponent["current_hp"])

        if self.resolved:
            xp_gain = calc_battle_xp(opponent["level"], opponent["rarity"])
            result = self.pokemon_cog.apply_xp_gain(
                actor["owner_id"],
                actor["pokemon_id"],
                actor["level"],
                actor["xp"],
                xp_gain
            )
            if result:
                new_level, new_xp, levels_gained = result
                old_stats = actor["stats"]
                actor["level"] = new_level
                actor["xp"] = new_xp
                if levels_gained > 0:
                    species = get_species(actor["species_id"]) or {}
                    new_stats = calc_stats(species, new_level, actor["ivs"], actor["trait"])
                    delta_hp = max(0, new_stats["hp"] - old_stats["hp"])
                    actor["stats"] = new_stats
                    actor["max_hp"] = new_stats["hp"]
                    actor["current_hp"] = min(new_stats["hp"], actor["current_hp"] + delta_hp)
                if levels_gained > 0:
                    log_lines.append(f"Level up! +{levels_gained} levels.")

        embed = self.build_embed(log_lines)
        if interaction.response.is_done():
            await interaction.message.edit(embed=embed, view=self)
        else:
            await interaction.response.edit_message(embed=embed, view=self)
