import discord
from discord.ext import commands
import random
import asyncio
from utils.economy_helpers import update_wallet, get_balance
from utils.database import db
from utils.config import get_guild_config
from cogs.adventure.engine import (
    get_character, update_character, calculate_damage,
    generate_enemy, generate_boss, process_bleed, apply_on_kill_stat, is_demon,
    award_dungeon_xp, calculate_xp_reward, xp_for_next_level
)
from cogs.adventure.items import (
    get_equipped_bonuses, get_equipped_items, get_inventory, equip_item, give_item,
    roll_loot, get_item, ITEMS, RARITY_COLORS, RARITY_DISPLAY, ALL_SLOTS
)
from utils.consumables import CONSUMABLES, get_consumables, use_consumable, add_consumable

CMD_HINT = "💡 !inventory | !equip | !use | !shop | !leave"

class SimpleContext:
    """Lightweight ctx-like object for interaction-driven flows."""
    def __init__(self, user):
        self.author = user


class TextInteraction:
    """Adapter that wraps a commands.Context to behave like a discord.Interaction.
    
    This allows CombatView/AdventureView methods (designed for button interactions)
    to also work with text commands (!attack, !explore, !flee) without duplicating logic.
    """
    def __init__(self, ctx):
        self.ctx = ctx
        self.user = ctx.author
        self.guild = ctx.guild
        self._channel = ctx.channel
        self._deferred = False

        # Create a fake message object with .channel and .embeds for compat
        class _FakeMessage:
            def __init__(self, channel):
                self.channel = channel
                self.embeds = []
        self.message = _FakeMessage(ctx.channel)

        class _Response:
            def __init__(self, parent):
                self._parent = parent
            async def defer(self):
                self._parent._deferred = True
            async def send_message(self, content, ephemeral=False):
                await self._parent.ctx.send(content)
            async def edit_message(self, **kwargs):
                msg = await self._parent.ctx.send(**kwargs)
                self._parent.message = msg
        
        self.response = _Response(self)

    async def edit_original_response(self, **kwargs):
        """Send as a new message (text commands can't edit)."""
        msg = await self.ctx.send(**kwargs)
        self.message = msg



async def send_to_log_channel(bot, guild, message: str):
    """Send a message to the logs channel if configured."""
    import logging
    log = logging.getLogger("bot")
    if not guild:
        log.debug("[LogChannel] No guild provided, skipping")
        return
    ch_id = get_guild_config(guild.id, "logs_channel")
    if not ch_id:
        log.debug(f"[LogChannel] No logs_channel configured for guild {guild.id}")
        return
    ch = bot.get_channel(int(ch_id))
    if not ch:
        log.warning(f"[LogChannel] Channel {ch_id} not found in guild {guild.name}")
        return
    try:
        await ch.send(message)
    except Exception as e:
        log.warning(f"[LogChannel] Failed to send to logs channel: {e}")


class RestartView(discord.ui.View):
    """View with a single Restart button, sent after timeout."""
    def __init__(self, cog, user):
        super().__init__(timeout=None)  # Persistent — no auto-expiry
        self.cog = cog
        self.user = user

    @discord.ui.button(label="Restart Adventure", style=discord.ButtonStyle.success, emoji="🔄")
    async def restart(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.user.id:
            return
        # Clean up old timeout message
        self.cog._timeout_messages.pop(self.user.id, None)
        self.cog.active_adventures.add(self.user.id)
        for c in self.children:
            c.disabled = True
        await interaction.response.edit_message(content="🔄 Restarting...", view=self)
        ctx = SimpleContext(self.user)
        await self.cog.show_main_menu(interaction.channel, ctx)
        self.stop()


class AdventureView(discord.ui.View):
    def __init__(self, cog, ctx, char):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.char = char
        self._message = None
        self._left = False  # Set True when user leaves voluntarily

    async def on_timeout(self):
        """Delete old message, send one persistent restart prompt."""
        self.cog.active_adventures.discard(self.ctx.author.id)
        if self._left:
            return
        # Delete the old game message
        try:
            if self._message:
                await self._message.delete()
        except Exception:
            pass
        # Send ONE restart message (persistent, no auto-delete)
        try:
            ch = self._message.channel if self._message else None
            if ch:
                old_msg = self.cog._timeout_messages.pop(self.ctx.author.id, None)
                if old_msg:
                    try:
                        await old_msg.delete()
                    except Exception:
                        pass
                view = RestartView(self.cog, self.ctx.author)
                msg = await ch.send(
                    f"⏰ {self.ctx.author.mention} Your adventure session timed out.\n"
                    f"Click below to restart or type `!adventure`.",
                    view=view
                )
                self.cog._timeout_messages[self.ctx.author.id] = msg
        except Exception:
            pass

    @discord.ui.button(label="Explore", style=discord.ButtonStyle.danger, emoji="⚔️")
    async def explore(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        await self.cog.explore_room(interaction, self.char)

    @discord.ui.button(label="Rest", style=discord.ButtonStyle.success, emoji="💊")
    async def rest(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        bal = get_balance(self.ctx.author.id)
        heal_cost = 50
        if bal['wallet'] < heal_cost:
            await interaction.response.send_message(f"You need {heal_cost} 🪙 to rest.", ephemeral=True)
            return
        update_wallet(self.ctx.author.id, -heal_cost)
        bonuses = get_equipped_bonuses(self.ctx.author.id)
        effective_hp = self.char['max_hp'] + bonuses['max_hp']
        old_hp = self.char['hp']
        heal = int(effective_hp * 0.3)
        self.char['hp'] = min(effective_hp, self.char['hp'] + heal)
        actual_heal = self.char['hp'] - old_hp
        update_character(self.ctx.author.id, hp=self.char['hp'])

        hp_bar = CombatView.hp_bar(self.char['hp'], effective_hp)
        embed = discord.Embed(title="💊 Rested", color=0x2ECC71)
        bal = get_balance(self.ctx.author.id)
        embed.description = (
            f"{hp_bar} {self.char['hp']}/{effective_hp}\n"
            f"Healed **{actual_heal} HP** (-{heal_cost} 🪙)\n\n"
            f"Floor: {self.char['current_floor']}  |  Coins: {bal['wallet']:,} 🪙"
        )
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Stats", style=discord.ButtonStyle.secondary, emoji="📊")
    async def stats(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        bonuses = get_equipped_bonuses(self.ctx.author.id)
        def fmt(base, bonus):
            return f"{base}" + (f" (+{bonus})" if bonus > 0 else (f" ({bonus})" if bonus < 0 else ""))
        stats_text = (
            f"**Floor:** {self.char['current_floor']}  |  **Max:** {self.char['deepest_floor']}\n"
            f"**HP:** {self.char['hp']}/{self.char['max_hp'] + bonuses['max_hp']}\n"
            f"**ATK:** {fmt(self.char['base_attack'], bonuses['attack'])}\n"
            f"**DEF:** {fmt(self.char['base_defense'], bonuses['defense'])}\n"
            f"**SPD:** {fmt(self.char['base_speed'], bonuses['speed'])}\n"
            f"**LCK:** {fmt(self.char['base_luck'], bonuses['luck'])}\n"
            f"**Bosses Killed:** {self.char['bosses_killed']}\n"
            f"**Deaths:** {self.char['total_deaths']}"
        )
        if bonuses['lifesteal'] > 0:
            stats_text += f"\n🩸 Lifesteal: {bonuses['lifesteal']*100:.0f}%"
        if bonuses['magic_dodge'] > 0:
            stats_text += f"\n⏳ Magic Dodge: {bonuses['magic_dodge']*100:.0f}%"
        if bonuses['crit_chance_bonus'] > 0:
            stats_text += f"\n👻 Crit Bonus: +{bonuses['crit_chance_bonus']*100:.0f}%"
        stats_text += (
            "\n\n**— What Stats Do —**\n"
            "```\n"
            "⚔️ ATK — Damage dealt to enemies\n"
            "🛡️ DEF — Reduces incoming damage\n"
            "💨 SPD — Flee success rate\n"
            "🍀 LCK — Critical hit chance\n"
            "❤️ HP  — Health. 0 = death.\n"
            "```"
        )
        await interaction.response.send_message(stats_text, ephemeral=True)


class CombatView(discord.ui.View):
    def __init__(self, cog, ctx, char, enemy, bonuses):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.char = char
        self.enemy = enemy
        self.bonuses = bonuses
        self.turn = 0
        self.combat_log = []
        self.potions_used = 0
        self.max_potions = 10
        self.shield_active = False  # Shield Scroll flag
        self._message = None
        self._left = False  # Set True when superseded or user leaves

    async def on_timeout(self):
        """Delete old message, send one persistent restart prompt."""
        self.cog.active_combats.pop(self.ctx.author.id, None)
        self.cog.active_adventures.discard(self.ctx.author.id)
        if self._left:
            return
        # Delete the old combat message
        try:
            if self._message:
                await self._message.delete()
        except Exception:
            pass
        # Send ONE restart message (persistent, no auto-delete)
        try:
            ch = self._message.channel if self._message else None
            if ch:
                old_msg = self.cog._timeout_messages.pop(self.ctx.author.id, None)
                if old_msg:
                    try:
                        await old_msg.delete()
                    except Exception:
                        pass
                view = RestartView(self.cog, self.ctx.author)
                msg = await ch.send(
                    f"⏰ {self.ctx.author.mention} Combat timed out.\n"
                    f"Type `!adventure` to re-enter.",
                    view=view
                )
                self.cog._timeout_messages[self.ctx.author.id] = msg
        except Exception:
            pass

    @staticmethod
    def hp_bar(current, maximum, length=10):
        """Generate a visual HP bar."""
        current = max(0, current)
        ratio = current / max(1, maximum)
        filled = round(ratio * length)
        empty = length - filled
        if ratio > 0.6:
            bar_char = "🟩"
        elif ratio > 0.3:
            bar_char = "🟨"
        else:
            bar_char = "🟥"
        return bar_char * filled + "⬛" * empty

    def get_embed_color(self):
        """Color based on player HP state."""
        ratio = self.char['hp'] / max(1, self.char['max_hp'])
        if ratio > 0.6:
            return 0x2ECC71  # green
        elif ratio > 0.3:
            return 0xF1C40F  # yellow
        else:
            return 0xE74C3C  # red

    def type_emoji(self):
        t = self.enemy.get('type', 'humanoid')
        return {"beast": "🐾", "undead": "💀", "humanoid": "👤", "slime": "🧪", "demon": "😈"}.get(t, "❓")

    def update_embed(self, embed):
        self.turn += 1
        is_boss = self.enemy.get('is_boss', False)
        floor = self.char['current_floor']
        effective_hp = self.char['max_hp'] + self.bonuses.get('max_hp', 0)

        # Boss progress indicator
        next_boss = ((floor // 10) + 1) * 10
        floors_to_boss = next_boss - floor
        boss_str = f"👑 **BOSS FIGHT**" if is_boss else f"Boss in {floors_to_boss} floors"

        # Enemy section
        e_hp = max(0, self.enemy['hp'])
        e_bar = self.hp_bar(e_hp, self.enemy['max_hp'])
        bleed_str = f"  🩸×{self.enemy['bleed_stacks']}" if self.enemy.get('bleed_stacks', 0) > 0 else ""

        # Player section
        p_bar = self.hp_bar(self.char['hp'], effective_hp)

        # Flee chance preview
        flee_chance = min(0.95, (self.char['base_speed'] + self.bonuses['speed']) / max(1, self.enemy['speed']) * 0.6)

        embed.color = self.get_embed_color()
        embed.title = f"{'👑 BOSS' if is_boss else '⚔️ COMBAT'} — Floor {floor}"

        desc = (
            f"**{self.enemy['name']}** {self.type_emoji()} Lv.{self.enemy['level']}\n"
            f"{e_bar} {e_hp}/{self.enemy['max_hp']}{bleed_str}\n"
            f"`ATK {self.enemy['attack']}  DEF {self.enemy['defense']}  SPD {self.enemy['speed']}`\n\n"
            f"{self.ctx.author.mention} ⚔️\n"
            f"{p_bar} {max(0, self.char['hp'])}/{effective_hp}\n"
        )

        # Active effects line
        effects = []
        if self.bonuses.get('lifesteal', 0) > 0:
            effects.append(f"🩸{self.bonuses['lifesteal']*100:.0f}%")
        if self.bonuses.get('magic_dodge', 0) > 0:
            effects.append(f"⏳{self.bonuses['magic_dodge']*100:.0f}%")
        if self.bonuses.get('crit_chance_bonus', 0) > 0:
            effects.append(f"👻+{self.bonuses['crit_chance_bonus']*100:.0f}%crit")
        if self.bonuses.get('execute_chance', 0) > 0:
            effects.append(f"⭐{self.bonuses['execute_chance']*100:.0f}%exec")
        if self.bonuses.get('bleed_on_hit', 0) > 0:
            effects.append(f"🗡️bleed")
        if self.bonuses.get('damage_bonus', 0) > 0:
            effects.append(f"🔱+{self.bonuses['damage_bonus']*100:.0f}%dmg")
        if effects:
            desc += f"`{'  '.join(effects)}`\n"

        # Combat log (last 4 entries)
        if self.combat_log:
            desc += "\n**— Combat Log —**\n"
            for entry in self.combat_log[-4:]:
                desc += f"{entry}\n"

        desc += f"\n`Turn {self.turn}`  •  `🏃 Flee: {flee_chance*100:.0f}%`  •  `{boss_str}`"
        embed.description = desc
        embed.set_footer(text=f"⚔️ !attack | 🏃 !flee | 🧪 Potions: {self.potions_used}/{self.max_potions} used")

    async def do_player_attack(self, interaction):
        await interaction.response.defer()
        b = self.bonuses

        # Starforger's Favor — 3% chance to halve enemy HP
        if b['execute_chance'] > 0 and random.random() < b['execute_chance']:
            halved = self.enemy['hp'] // 2
            self.enemy['hp'] -= halved
            self.combat_log.append(f"⭐ **Starforger's Favor!** Enemy HP halved (-{halved})!")

        # Spear of the Red Dragon — self damage
        if b['self_damage_pct'] > 0:
            self_dmg = max(1, int(self.char['max_hp'] * b['self_damage_pct']))
            self.char['hp'] -= self_dmg
            update_character(self.ctx.author.id, hp=self.char['hp'])
            self.combat_log.append(f"🔱 The Spear burns you for {self_dmg}.")

        # Player attack
        total_atk = self.char['base_attack'] + b['attack']
        total_luck = self.char['base_luck'] + b['luck']
        dmg, is_crit = calculate_damage(
            total_atk, self.enemy['defense'], total_luck,
            crit_chance_bonus=b['crit_chance_bonus'],
            crit_damage_bonus=b['crit_damage_bonus'],
            damage_bonus=b['damage_bonus']
        )
        self.enemy['hp'] -= dmg
        crit_str = " 💥**CRIT**" if is_crit else ""
        self.combat_log.append(f"⚔️ You deal **{dmg}**{crit_str}")

        # Bleed on hit (Dagger and Pike)
        if b['bleed_on_hit'] > 0:
            self.enemy['bleed_stacks'] = self.enemy.get('bleed_stacks', 0) + b['bleed_on_hit']
            self.combat_log.append(f"🩸 +{b['bleed_on_hit']} bleed stacks")

        # Lifesteal
        if b['lifesteal'] > 0:
            heal = max(1, int(dmg * b['lifesteal']))
            effective_hp = self.char['max_hp'] + self.bonuses.get('max_hp', 0)
            self.char['hp'] = min(effective_hp, self.char['hp'] + heal)
            update_character(self.ctx.author.id, hp=self.char['hp'])
            self.combat_log.append(f"🩸 Healed {heal} HP")

        # Process bleed on enemy
        bleed_dmg = process_bleed(self.enemy)
        if bleed_dmg > 0:
            self.combat_log.append(f"🩸 Enemy bleeds for {bleed_dmg}")

        if self.enemy['hp'] <= 0:
            await self.handle_victory(interaction)
        else:
            await self.enemy_turn(interaction)

    async def handle_victory(self, interaction):
        floor = self.char['current_floor']
        is_boss = self.enemy.get('is_boss', False)
        reward = random.randint(5, 15) * floor

        # Boss bonus
        if is_boss:
            reward = int(reward * 1.5)

        update_wallet(self.ctx.author.id, reward)

        # On-kill stat gain (Blackened Sword)
        stat_msg = apply_on_kill_stat(
            self.ctx.author.id, self.char,
            is_demon(self.enemy.get('name', '').replace('👑 ', '')),
            self.bonuses
        )

        self.char['current_floor'] += 1
        if self.char['current_floor'] > self.char['deepest_floor']:
            self.char['deepest_floor'] = self.char['current_floor']

        # Boss kill tracking
        if is_boss:
            self.char['bosses_killed'] = self.char.get('bosses_killed', 0) + 1
            update_character(self.ctx.author.id, bosses_killed=self.char['bosses_killed'])

        update_character(self.ctx.author.id, current_floor=self.char['current_floor'], deepest_floor=self.char['deepest_floor'])

        # Loot roll
        loot_id = roll_loot(floor, is_boss=is_boss)
        loot_lines = []
        if loot_id:
            _, auto_equipped = give_item(self.ctx.author.id, loot_id)
            loot_data = get_item(loot_id)
            rarity_icon = RARITY_COLORS.get(loot_data['rarity'], '⬜')
            equip_str = " *(auto-equipped)*" if auto_equipped else ""
            loot_lines.append(f"{rarity_icon} {loot_data['emoji']} **{loot_data['name']}** ({RARITY_DISPLAY[loot_data['rarity']]}){equip_str}")
            # Show stats
            stat_parts = []
            for stat, val in loot_data.get('stats', {}).items():
                sign = "+" if val > 0 else ""
                stat_parts.append(f"{sign}{val} {stat.upper()}")
            if stat_parts:
                loot_lines.append(f"  `{' / '.join(stat_parts)}`")
            # Legendary drop notification
            if loot_data['rarity'] == 'legendary':
                guild = interaction.guild
                if guild:
                    await send_to_log_channel(
                        self.cog.bot, guild,
                        f"⭐ **{self.ctx.author.display_name}** obtained **{loot_data['name']}** on Floor {floor}!"
                    )

        # Build victory embed
        embed = discord.Embed(
            title=f"🎉 {'BOSS ' if is_boss else ''}VICTORY — Floor {floor}",
            color=0x2ECC71
        )

        # XP reward
        xp_earned = calculate_xp_reward(self.enemy, floor, is_boss=is_boss)
        level_up_msg = award_dungeon_xp(self.ctx.author.id, self.char, xp_earned)

        lines = [
            f"{self.ctx.author.mention} — **{self.enemy['name']}** defeated!\n",
            f"💰 **+{reward:,} 🪙**  •  ⭐ **+{xp_earned} XP**  •  Floor {self.char['current_floor']}",
        ]
        if level_up_msg:
            lines.append(f"\n{level_up_msg}")
        if stat_msg:
            lines.append(f"📈 {stat_msg}")
        if loot_lines:
            lines.append(f"\n**— Loot —**")
            lines.extend(loot_lines)
        else:
            lines.append("*No loot this time.*")

        embed.description = "\n".join(lines)
        for c in self.children: c.disabled = True
        self._left = True  # Prevent timeout message since combat ended normally
        await interaction.edit_original_response(embed=embed, view=self)

        # Floor milestone check
        guild = interaction.guild
        for milestone in [10, 25, 50]:
            if self.char['deepest_floor'] == milestone + 1:
                if guild:
                    await send_to_log_channel(
                        self.cog.bot, guild,
                        f"🏔️ **{self.ctx.author.display_name}** cleared Floor {milestone}!"
                    )

        await asyncio.sleep(2)
        self.cog.active_combats.pop(self.ctx.author.id, None)
        await self.cog.show_main_menu(interaction.message.channel, self.ctx, self.char)
        self.stop()

    async def enemy_turn(self, interaction):
        # Shield Scroll check — blocks one attack completely
        if self.shield_active:
            self.shield_active = False
            self.combat_log.append("🛡️ **Shield absorbed the attack!**")
        # Magic dodge check (Hourglass of the Archiver)
        elif self.bonuses['magic_dodge'] > 0 and random.random() < self.bonuses['magic_dodge']:
            self.combat_log.append(f"⏳ Hourglass shimmers — attack dodged!")
        else:
            total_def = self.char['base_defense'] + self.bonuses['defense']
            dmg, is_crit = calculate_damage(self.enemy['attack'], total_def)
            self.char['hp'] -= dmg
            crit_str = " 💥**CRIT**" if is_crit else ""
            self.combat_log.append(f"🔻 {self.enemy['name']} deals **{dmg}**{crit_str}")

        update_character(self.ctx.author.id, hp=self.char['hp'])
        # Get existing embed or create fresh one (for text command path)
        try:
            embed = interaction.message.embeds[0]
        except (AttributeError, IndexError, TypeError):
            embed = discord.Embed()
        self.update_embed(embed)

        if self.char['hp'] <= 0:
            # Death
            self.char['total_deaths'] += 1
            self.char['hp'] = self.char['max_hp']
            old_floor = self.char['current_floor']

            # Revival Token check
            has_revival = use_consumable(self.ctx.author.id, "revival_token")
            if has_revival:
                self.char['current_floor'] = old_floor  # No floor loss
                floor_penalty = 0
            else:
                floor_penalty = 5
                self.char['current_floor'] = max(1, self.char['current_floor'] - floor_penalty)

            update_character(self.ctx.author.id, hp=self.char['hp'], current_floor=self.char['current_floor'], total_deaths=self.char['total_deaths'])
            bal = get_balance(self.ctx.author.id)
            tax = int(bal['wallet'] * 0.15)
            update_wallet(self.ctx.author.id, -tax)

            death_embed = discord.Embed(title="💀 YOU DIED", color=0x000000)
            if has_revival:
                death_embed.description = (
                    f"{self.ctx.author.mention} — **{self.enemy['name']}** killed you on Floor {old_floor}.\n\n"
                    f"💀 **Revival Token** consumed! Floor loss prevented.\n"
                    f"```\n"
                    f"Floor:   {old_floor} (saved)\n"
                    f"Tax:     -{tax:,} 🪙  (15% wallet)\n"
                    f"HP:      Fully restored\n"
                    f"```\n"
                    f"*Type `!adventure` to try again.*"
                )
            else:
                death_embed.description = (
                    f"{self.ctx.author.mention} — **{self.enemy['name']}** killed you on Floor {old_floor}.\n\n"
                    f"```\n"
                    f"Floor:   {old_floor} → {self.char['current_floor']}  (-{floor_penalty})\n"
                    f"Tax:     -{tax:,} 🪙  (15% wallet)\n"
                    f"HP:      Fully restored\n"
                    f"Deaths:  {self.char['total_deaths']} total\n"
                    f"```\n"
                    f"*Type `!adventure` to try again.*"
                )
            for c in self.children: c.disabled = True
            self._left = True  # Prevent timeout message since combat ended (death)
            await interaction.edit_original_response(embed=death_embed, view=self)
            # Kill feed
            guild = interaction.guild
            if guild:
                await send_to_log_channel(
                    self.cog.bot, guild,
                    f"💀 **{self.ctx.author.display_name}** was slain by {self.enemy['name']} on Floor {old_floor}."
                )
            self.cog.active_combats.pop(self.ctx.author.id, None)
            self.cog.active_adventures.discard(self.ctx.author.id)
            self.stop()
        else:
            await interaction.edit_original_response(embed=embed, view=self)

    @discord.ui.button(label="Attack", style=discord.ButtonStyle.danger, emoji="⚔️")
    async def attack(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your battle.", ephemeral=True)
            return
        await self.do_player_attack(interaction)

    async def do_flee(self, interaction):
        """Shared flee logic — used by both button and text command."""
        await interaction.response.defer()
        flee_chance = min(0.95, (self.char['base_speed'] + self.bonuses['speed']) / max(1, self.enemy['speed']) * 0.6)
        if random.random() < flee_chance:
            embed = discord.Embed(title="🏃 Fled!", color=0xF1C40F)
            embed.description = f"{self.ctx.author.mention} escaped from **{self.enemy['name']}**."
            for c in self.children: c.disabled = True
            self._left = True  # Prevent timeout message since combat ended (fled)
            await interaction.edit_original_response(embed=embed, view=self)
            await asyncio.sleep(2)
            self.cog.active_combats.pop(self.ctx.author.id, None)
            await self.cog.show_main_menu(interaction.message.channel, self.ctx, self.char)
            self.stop()
        else:
            self.combat_log.append(f"🏃 Flee failed! ({flee_chance*100:.0f}% chance)")
            await self.enemy_turn(interaction)

    @discord.ui.button(label="Flee", style=discord.ButtonStyle.secondary, emoji="🏃")
    async def flee(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            return
        await self.do_flee(interaction)

    async def do_use_potion(self, interaction, potion_type="health_potion"):
        """Use a potion during combat — heals self.char directly to stay in sync."""
        if self.potions_used >= self.max_potions:
            await interaction.response.send_message(
                f"🧪 Potion limit reached! ({self.max_potions}/{self.max_potions} used this battle)", ephemeral=True)
            return
        stock = get_consumables(self.ctx.author.id)
        if stock.get(potion_type, 0) <= 0:
            name = CONSUMABLES[potion_type]['name']
            await interaction.response.send_message(
                f"You don't have any **{name}**. Buy from `!shop consumables`.", ephemeral=True)
            return
        effective_hp = self.char['max_hp'] + self.bonuses.get('max_hp', 0)
        if self.char['hp'] >= effective_hp:
            await interaction.response.send_message("You're already at full HP!", ephemeral=True)
            return
        await interaction.response.defer()
        use_consumable(self.ctx.author.id, potion_type)
        self.potions_used += 1
        old_hp = self.char['hp']
        if potion_type == "greater_potion":
            self.char['hp'] = effective_hp
        else:
            self.char['hp'] = min(effective_hp, self.char['hp'] + 50)
        actual_heal = self.char['hp'] - old_hp
        update_character(self.ctx.author.id, hp=self.char['hp'])
        potion_name = CONSUMABLES[potion_type]['name']
        self.combat_log.append(f"🧪 **{potion_name}** — +{actual_heal} HP ({self.potions_used}/{self.max_potions})")
        try:
            embed = interaction.message.embeds[0]
        except (AttributeError, IndexError):
            embed = discord.Embed()
        self.update_embed(embed)
        await interaction.edit_original_response(embed=embed, view=self)

    @discord.ui.button(label="Potion", style=discord.ButtonStyle.success, emoji="🧪")
    async def potion(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            return
        await self.do_use_potion(interaction, "health_potion")



class Adventure(commands.Cog):
    """Text-based RPG Adventure"""
    def __init__(self, bot):
        self.bot = bot
        self.active_adventures = set()
        self.active_combats = {}  # user_id -> CombatView
        self._timeout_messages = {}  # user_id -> Message (latest timeout prompt)
        self._adventure_views = {}  # user_id -> AdventureView (for marking _left)

    async def cog_check(self, ctx):
        """Enforce dungeon channel for all adventure commands."""
        from cogs.games.utils import check_channel
        return await check_channel(ctx, "dungeon_channel")

    async def show_main_menu(self, channel, ctx, char=None):
        # Retire any previous adventure view so its on_timeout won't fire
        old_adv = self._adventure_views.pop(ctx.author.id, None)
        if old_adv:
            old_adv._left = True
            old_adv.stop()

        if not char:
            char = get_character(ctx.author.id)
        bal = get_balance(ctx.author.id)
        bonuses = get_equipped_bonuses(ctx.author.id)
        effective_hp = char['max_hp'] + bonuses['max_hp']
        floor = char['current_floor']

        # Boss progress
        next_boss = ((floor // 10) + 1) * 10
        floors_to_boss = next_boss - floor
        is_boss_next = floors_to_boss == 0
        boss_str = "👑 **BOSS NEXT!**" if is_boss_next else f"Boss in {floors_to_boss} floors"

        # HP bar
        hp_bar = CombatView.hp_bar(char['hp'], effective_hp)

        # Low HP warning + potion suggestion
        hp_ratio = char['hp'] / max(1, effective_hp)
        hp_warning = ""
        if hp_ratio <= 0.4:
            stock = get_consumables(ctx.author.id)
            has_potion = stock.get('health_potion', 0) > 0 or stock.get('greater_potion', 0) > 0
            if has_potion:
                hp_warning = "\n⚠️ **LOW HP** — You have potions! Use `!use health potion`"
            else:
                hp_warning = "\n⚠️ **LOW HP** — Rest or buy potions at `!shop`"

        # Equipped gear summary (5 slots)
        equipped = get_equipped_items(ctx.author.id)
        gear_parts = []
        for slot in ALL_SLOTS:
            if slot in equipped:
                item = equipped[slot]
                rarity_icon = RARITY_COLORS.get(item['rarity'], '⬜')
                gear_parts.append(f"{rarity_icon} {item['emoji']} {item['name']}")
            else:
                gear_parts.append(f"⬛ *{slot}*")
        gear_str = "  |  ".join(gear_parts)

        # Dungeon level + XP
        d_level = char.get('dungeon_level', 1)
        d_xp = char.get('dungeon_xp', 0)
        d_xp_next = xp_for_next_level(d_level)
        xp_ratio = d_xp / max(1, d_xp_next)
        xp_filled = round(xp_ratio * 10)
        xp_bar = "🟦" * xp_filled + "⬛" * (10 - xp_filled)

        embed = discord.Embed(title=f"🗡️ {ctx.author.display_name}'s Dungeon", color=0x3498DB if hp_ratio > 0.3 else 0xE74C3C)
        embed.description = (
            f"{ctx.author.mention} — Floor {floor}\n"
            f"{hp_bar} {char['hp']}/{effective_hp} HP  |  {bal['wallet']:,} 🪙\n"
            f"⭐ **Lv.{d_level}**  {xp_bar} {d_xp}/{d_xp_next} XP\n\n"
            f"⚔️ `ATK {char['base_attack']+bonuses['attack']}`  "
            f"🛡️ `DEF {char['base_defense']+bonuses['defense']}`  "
            f"💨 `SPD {char['base_speed']+bonuses['speed']}`  "
            f"🍀 `LCK {char['base_luck']+bonuses['luck']}`\n\n"
            f"**Gear:** {gear_str}\n\n"
            f"{boss_str}  •  Best: Floor {char['deepest_floor']}"
            f"{hp_warning}"
        )
        embed.set_footer(text=CMD_HINT)
        view = AdventureView(self, ctx, char)
        msg = await channel.send(embed=embed, view=view)
        view._message = msg  # Store for timeout editing
        self._adventure_views[ctx.author.id] = view  # Track for leave command

    async def explore_room(self, interaction, char):
        await interaction.response.defer()

        # Retire the current adventure view since we're transitioning
        old_adv = self._adventure_views.pop(interaction.user.id, None)
        if old_adv:
            old_adv._left = True
            old_adv.stop()

        roll = random.random()
        floor = char['current_floor']
        bonuses = get_equipped_bonuses(interaction.user.id)

        # Boss every 10 floors
        is_boss_floor = floor % 10 == 0 and floor > 0

        if is_boss_floor or roll < 0.50:
            # Retire any old combat view
            old_combat = self.active_combats.pop(interaction.user.id, None)
            if old_combat:
                old_combat._left = True
                old_combat.stop()

            enemy = generate_boss(floor) if is_boss_floor else generate_enemy(floor)
            embed = discord.Embed(
                title=f"{'👑 BOSS' if is_boss_floor else '⚔️ COMBAT'} — Floor {floor}",
                color=0xFF0000 if is_boss_floor else 0xE74C3C
            )
            fake_ctx = SimpleContext(interaction.user)
            view = CombatView(self, fake_ctx, char, enemy, bonuses)
            view.update_embed(embed)
            self.active_combats[interaction.user.id] = view
            await interaction.edit_original_response(embed=embed, view=view)
            try:
                view._message = await interaction.original_response()
            except Exception:
                pass

        elif roll < 0.70:
            coins = random.randint(20, 80) * floor
            update_wallet(interaction.user.id, coins)
            # Treasure may also contain loot
            loot_id = roll_loot(floor)
            loot_msg = ""
            if loot_id:
                _, auto_equipped = give_item(interaction.user.id, loot_id)
                loot_data = get_item(loot_id)
                rarity_icon = RARITY_COLORS.get(loot_data['rarity'], '⬜')
                equip_str = " *(auto-equipped)*" if auto_equipped else ""
                stat_parts = [f"+{v} {k.upper()}" if v > 0 else f"{v} {k.upper()}" for k, v in loot_data.get('stats', {}).items()]
                stat_str = f"\n  `{' / '.join(stat_parts)}`" if stat_parts else ""
                loot_msg = f"\n{rarity_icon} {loot_data['emoji']} **{loot_data['name']}**{equip_str}{stat_str}"

            embed = discord.Embed(title="💰 Treasure Room!", color=0xF1C40F)
            embed.description = f"You found an ancient chest. Inside was {coins:,} 🪙!{loot_msg}\n\n*(Advancing...)*"
            char['current_floor'] += 1
            if char['current_floor'] > char['deepest_floor']:
                char['deepest_floor'] = char['current_floor']
            update_character(interaction.user.id, current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])
            await interaction.edit_original_response(embed=embed, view=None)
            await asyncio.sleep(3)
            await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

        elif roll < 0.85:
            dmg = random.randint(10, 20) + floor
            char['hp'] -= dmg
            effective_hp = char['max_hp'] + bonuses.get('max_hp', 0)
            embed = discord.Embed(title="⚠️ TRAP!", color=0xE74C3C)
            if char['hp'] <= 0:
                char['total_deaths'] += 1
                char['hp'] = char['max_hp']
                old_floor = char['current_floor']

                # Revival Token check
                has_revival = use_consumable(interaction.user.id, "revival_token")
                if has_revival:
                    char['current_floor'] = old_floor
                    floor_penalty = 0
                else:
                    floor_penalty = 5
                    char['current_floor'] = max(1, char['current_floor'] - floor_penalty)

                update_character(interaction.user.id, hp=char['hp'], current_floor=char['current_floor'], total_deaths=char['total_deaths'])
                bal = get_balance(interaction.user.id)
                tax = int(bal['wallet'] * 0.15)
                update_wallet(interaction.user.id, -tax)
                embed.color = 0x000000
                embed.title = "💀 YOU DIED"
                if has_revival:
                    embed.description = (
                        f"{interaction.user.mention} — A hidden trap dealt **{dmg} damage** and killed you.\n\n"
                        f"💀 **Revival Token** consumed! Floor loss prevented.\n"
                        f"```\n"
                        f"Floor:   {old_floor} (saved)\n"
                        f"Tax:     -{tax:,} 🪙  (15% wallet)\n"
                        f"HP:      Fully restored\n"
                        f"```\n"
                        f"*Type `!adventure` to try again.*"
                    )
                else:
                    embed.description = (
                        f"{interaction.user.mention} — A hidden trap dealt **{dmg} damage** and killed you.\n\n"
                        f"```\n"
                        f"Floor:   {old_floor} → {char['current_floor']}  (-{floor_penalty})\n"
                        f"Tax:     -{tax:,} 🪙  (15% wallet)\n"
                        f"HP:      Fully restored\n"
                        f"Deaths:  {char['total_deaths']} total\n"
                        f"```\n"
                        f"*Type `!adventure` to try again.*"
                    )
                await interaction.edit_original_response(embed=embed, view=None)
                self.active_adventures.discard(interaction.user.id)
            else:
                update_character(interaction.user.id, hp=char['hp'])
                hp_bar = CombatView.hp_bar(char['hp'], effective_hp)
                embed.description = (
                    f"A hidden trap dealt **{dmg} damage**!\n\n"
                    f"{hp_bar} {char['hp']}/{effective_hp} HP\n\n"
                    f"*(Advancing...)*"
                )
                await interaction.edit_original_response(embed=embed, view=None)
                await asyncio.sleep(3)
                await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

        elif roll < 0.95:
            # Rest Shrine — free heal
            effective_hp = char['max_hp'] + bonuses.get('max_hp', 0)
            heal = int(effective_hp * 0.15)
            old_hp = char['hp']
            char['hp'] = min(effective_hp, char['hp'] + heal)
            actual_heal = char['hp'] - old_hp
            char['current_floor'] += 1
            if char['current_floor'] > char['deepest_floor']:
                char['deepest_floor'] = char['current_floor']
            update_character(interaction.user.id, hp=char['hp'], current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])
            hp_bar = CombatView.hp_bar(char['hp'], effective_hp)
            embed = discord.Embed(title="🛕 Rest Shrine", color=0x2ECC71)
            embed.description = (
                f"{interaction.user.mention}\n"
                f"You found a glowing shrine. Its warmth heals your wounds.\n\n"
                f"{hp_bar} {char['hp']}/{effective_hp} HP  (+{actual_heal})\n\n"
                f"*(Advancing...)*"
            )
            await interaction.edit_original_response(embed=embed, view=None)
            await asyncio.sleep(3)
            await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

        else:
            embed = discord.Embed(title="🕯️ Empty Room", color=0x95A5A6)
            embed.description = "The room is empty. You move on safely.\n\n*(Advancing...)*"
            char['current_floor'] += 1
            if char['current_floor'] > char['deepest_floor']:
                char['deepest_floor'] = char['current_floor']
            update_character(interaction.user.id, current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])
            await interaction.edit_original_response(embed=embed, view=None)
            await asyncio.sleep(2)
            await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

    # ── Entry ──
    @commands.command(aliases=['adv'])
    async def adventure(self, ctx):
        """Enter the dungeon or re-open the menu if stuck"""
        if ctx.author.id in self.active_adventures:
            # If stuck in combat, clear it and let them re-enter
            if ctx.author.id in self.active_combats:
                del self.active_combats[ctx.author.id]
            self.active_adventures.discard(ctx.author.id)
        self.active_adventures.add(ctx.author.id)
        await self.show_main_menu(ctx.channel, ctx)

    # ── Leave / Quit ──
    @commands.command(aliases=['quit', 'exitdungeon'])
    async def leave(self, ctx):
        """Leave the dungeon and unlock yourself"""
        was_active = ctx.author.id in self.active_adventures
        self.active_adventures.discard(ctx.author.id)
        # Mark the active adventure view as voluntarily left so timeout won't send a message
        adv_view = self._adventure_views.pop(ctx.author.id, None)
        if adv_view:
            adv_view._left = True
            adv_view.stop()
        # Mark the active combat view as voluntarily left too
        combat_view = self.active_combats.pop(ctx.author.id, None)
        if combat_view:
            combat_view._left = True
            combat_view.stop()
        # Clean up any existing timeout message
        old_msg = self._timeout_messages.pop(ctx.author.id, None)
        if old_msg:
            try:
                await old_msg.delete()
            except Exception:
                pass
        if was_active:
            await ctx.send(f"🚪 {ctx.author.mention} left the dungeon. Progress saved.")
        else:
            await ctx.send("You weren't in the dungeon.", delete_after=5)

    # ── Explore (text command) ──
    @commands.command()
    async def explore(self, ctx):
        """Explore the next room (text alternative to the Explore button)"""
        if ctx.author.id in self.active_combats:
            await ctx.send("⚔️ You're in combat! Use `!attack` or `!flee`.", delete_after=5)
            return
        if ctx.author.id not in self.active_adventures:
            self.active_adventures.add(ctx.author.id)
        char = get_character(ctx.author.id)
        text_interaction = TextInteraction(ctx)
        await self.explore_room(text_interaction, char)
    # ── Attack (text command) ──
    @commands.command(aliases=['atk', 'hit'])
    async def attack(self, ctx):
        """Attack the current enemy (text alternative to the Attack button)"""
        combat = self.active_combats.get(ctx.author.id)
        if not combat:
            await ctx.send("⚔️ Not in combat. Use `!explore` first.", delete_after=5)
            return
        text_interaction = TextInteraction(ctx)
        await combat.do_player_attack(text_interaction)

    # ── Flee (text command) ──
    @commands.command(aliases=['run'])
    async def flee(self, ctx):
        """Flee from current combat (text alternative to the Flee button)"""
        combat = self.active_combats.get(ctx.author.id)
        if not combat:
            await ctx.send("You're not in combat.", delete_after=5)
            return
        text_interaction = TextInteraction(ctx)
        await combat.do_flee(text_interaction)

    # ── Rest (text command) ──
    @commands.command()
    async def rest(self, ctx):
        """Heal 30% HP for 50 coins (text alternative to the Rest button)"""
        if ctx.author.id in self.active_combats:
            await ctx.send("❌ Can't rest during combat!", delete_after=5)
            return
        bal = get_balance(ctx.author.id)
        heal_cost = 50
        if bal['wallet'] < heal_cost:
            await ctx.send(f"You need {heal_cost} 🪙 to rest.", delete_after=5)
            return
        char = get_character(ctx.author.id)
        bonuses = get_equipped_bonuses(ctx.author.id)
        effective_hp = char['max_hp'] + bonuses['max_hp']
        update_wallet(ctx.author.id, -heal_cost)
        old_hp = char['hp']
        heal = int(effective_hp * 0.3)
        char['hp'] = min(effective_hp, char['hp'] + heal)
        actual_heal = char['hp'] - old_hp
        update_character(ctx.author.id, hp=char['hp'])
        hp_bar = CombatView.hp_bar(char['hp'], effective_hp)
        embed = discord.Embed(title="💊 Rested", color=0x2ECC71)
        bal = get_balance(ctx.author.id)
        embed.description = f"{ctx.author.mention}\n{hp_bar} {char['hp']}/{effective_hp}\nHealed **{actual_heal} HP** (-{heal_cost} 🪙)  |  {bal['wallet']:,} 🪙"
        await ctx.send(embed=embed)

    # ── Use Consumable ──
    @commands.command()
    async def use(self, ctx, *, item_name: str):
        """Use a consumable item from your inventory"""
        name_lower = item_name.lower().strip()

        # Find matching consumable
        target_id = None
        for cid, cdata in CONSUMABLES.items():
            if cdata['name'].lower() == name_lower:
                target_id = cid
                break

        if not target_id:
            await ctx.send("Unknown item. Check `!inventory` for your consumables.", delete_after=10)
            return

        # Revival token is passive — can't be manually used
        if target_id == "revival_token":
            await ctx.send("💀 Revival Tokens activate automatically on death. You don't need to use them manually.")
            return

        # Check stock
        stock = get_consumables(ctx.author.id)
        if stock.get(target_id, 0) <= 0:
            await ctx.send(f"You don't have any **{CONSUMABLES[target_id]['name']}**. Buy one at `!shop consumables`.")
            return

        # If in combat, sync with combat view to prevent stale HP overwrite
        combat = self.active_combats.get(ctx.author.id)
        if combat and target_id in ("health_potion", "greater_potion"):
            if combat.potions_used >= combat.max_potions:
                await ctx.send(f"🧪 Potion limit reached! ({combat.max_potions}/{combat.max_potions} used this battle)", delete_after=5)
                return
            effective_hp = combat.char['max_hp'] + combat.bonuses.get('max_hp', 0)
            if combat.char['hp'] >= effective_hp:
                await ctx.send("You're already at full HP!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.potions_used += 1
            old_hp = combat.char['hp']
            if target_id == "greater_potion":
                combat.char['hp'] = effective_hp
            else:
                combat.char['hp'] = min(effective_hp, combat.char['hp'] + 50)
            actual_heal = combat.char['hp'] - old_hp
            update_character(ctx.author.id, hp=combat.char['hp'])
            potion_name = CONSUMABLES[target_id]['name']
            combat.combat_log.append(f"🧪 **{potion_name}** — +{actual_heal} HP ({combat.potions_used}/{combat.max_potions})")
            hp_bar = CombatView.hp_bar(combat.char['hp'], effective_hp)
            embed = discord.Embed(title=f"🧪 {potion_name}", color=0x2ECC71)
            embed.description = f"{ctx.author.mention}\n{hp_bar} {combat.char['hp']}/{effective_hp}\nHealed **{actual_heal} HP** ({combat.potions_used}/{combat.max_potions} potions used)"
            await ctx.send(embed=embed)
            return

        char = get_character(ctx.author.id)
        bonuses = get_equipped_bonuses(ctx.author.id)
        effective_hp = char['max_hp'] + bonuses['max_hp']

        if target_id == "health_potion":
            if char['hp'] >= effective_hp:
                await ctx.send("You're already at full HP!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            old_hp = char['hp']
            char['hp'] = min(effective_hp, char['hp'] + 50)
            actual_heal = char['hp'] - old_hp
            update_character(ctx.author.id, hp=char['hp'])
            hp_bar = CombatView.hp_bar(char['hp'], effective_hp)
            embed = discord.Embed(title="🧪 Health Potion", color=0x2ECC71)
            embed.description = f"{ctx.author.mention}\n{hp_bar} {char['hp']}/{effective_hp}\nHealed **{actual_heal} HP**"
            await ctx.send(embed=embed)

        elif target_id == "greater_potion":
            if char['hp'] >= effective_hp:
                await ctx.send("You're already at full HP!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            old_hp = char['hp']
            char['hp'] = effective_hp
            actual_heal = char['hp'] - old_hp
            update_character(ctx.author.id, hp=char['hp'])
            hp_bar = CombatView.hp_bar(char['hp'], effective_hp)
            embed = discord.Embed(title="🧪 Greater Potion", color=0x2ECC71)
            embed.description = f"{ctx.author.mention}\n{hp_bar} {char['hp']}/{effective_hp}\nFully healed! **+{actual_heal} HP**"
            await ctx.send(embed=embed)

        elif target_id == "lucky_charm":
            use_consumable(ctx.author.id, target_id)
            combat = self.active_combats.get(ctx.author.id)
            if combat:
                combat.bonuses['luck'] = combat.bonuses.get('luck', 0) + 10
                combat._lucky_charm_turns = 5
            embed = discord.Embed(title="🍀 Lucky Charm", color=0xF1C40F)
            embed.description = f"{ctx.author.mention}\n**+10 LCK** for the next 5 combats!\n🍀 May the odds be ever in your favor."
            await ctx.send(embed=embed)

        # ===== COMBAT-ONLY CONSUMABLES =====
        elif target_id == "strength_tonic":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("💪 Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.bonuses['attack'] += 5
            combat.combat_log.append("💪 **Strength Tonic** — +5 ATK this fight!")
            await ctx.send("💪 **+5 ATK** for this combat!")

        elif target_id == "iron_skin":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("🛡️ Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.bonuses['defense'] += 5
            combat.combat_log.append("🛡️ **Iron Skin** — +5 DEF this fight!")
            await ctx.send("🛡️ **+5 DEF** for this combat!")

        elif target_id == "speed_elixir":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("💨 Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.bonuses['speed'] += 5
            combat.combat_log.append("💨 **Speed Elixir** — +5 SPD this fight!")
            await ctx.send("💨 **+5 SPD** for this combat!")

        elif target_id == "antidote":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("🩹 Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            # Antidote could clear bleed on player — but bleed is on enemies.
            # For now treat as a small heal (20 HP)
            old_hp = combat.char['hp']
            effective_hp = combat.char['max_hp'] + combat.bonuses.get('max_hp', 0)
            combat.char['hp'] = min(effective_hp, combat.char['hp'] + 20)
            actual = combat.char['hp'] - old_hp
            update_character(ctx.author.id, hp=combat.char['hp'])
            combat.combat_log.append(f"🩹 **Antidote** — healed {actual} HP")
            await ctx.send(f"🩹 Cleansed! Healed **{actual} HP**.")

        elif target_id == "smoke_bomb":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("💣 Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            # Guaranteed flee
            embed = discord.Embed(title="💣 Smoke Bomb!", color=0x95A5A6)
            embed.description = f"{ctx.author.mention} vanished in a cloud of smoke!"
            for c in combat.children: c.disabled = True
            try:
                if combat._message:
                    await combat._message.edit(embed=embed, view=combat)
            except Exception:
                pass
            await asyncio.sleep(1)
            self.active_combats.pop(ctx.author.id, None)
            await self.show_main_menu(ctx.channel, ctx, combat.char)

        elif target_id == "damage_scroll":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("📜 Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.enemy['hp'] -= 50
            combat.combat_log.append("📜 **Damage Scroll** — 50 damage to enemy!")
            await ctx.send("📜 The scroll erupts! **50 damage** to enemy!")

        elif target_id == "shield_scroll":
            combat = self.active_combats.get(ctx.author.id)
            if not combat:
                await ctx.send("🛡️ Can only be used during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            combat.shield_active = True
            combat.combat_log.append("🛡️ **Shield Scroll** — next attack blocked!")
            await ctx.send("🛡️ A magic shield surrounds you! Next enemy attack blocked.")

        # ===== NON-COMBAT CONSUMABLES =====
        elif target_id == "xp_tome":
            use_consumable(ctx.author.id, target_id)
            char = get_character(ctx.author.id)
            level_msg = award_dungeon_xp(ctx.author.id, char, 50)
            embed = discord.Embed(title="📖 XP Tome", color=0x9B59B6)
            desc = f"{ctx.author.mention}\n**+50 Dungeon XP!**"
            if level_msg:
                desc += f"\n{level_msg}"
            embed.description = desc
            await ctx.send(embed=embed)

        elif target_id == "floor_skip":
            if ctx.author.id in self.active_combats:
                await ctx.send("Can't skip floors during combat!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            char = get_character(ctx.author.id)
            char['current_floor'] += 1
            if char['current_floor'] > char['deepest_floor']:
                char['deepest_floor'] = char['current_floor']
            update_character(ctx.author.id, current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])
            await ctx.send(f"⏭️ Floor skipped! Now on **Floor {char['current_floor']}**.")

        elif target_id == "warp_crystal":
            if ctx.author.id in self.active_combats:
                await ctx.send("Can't warp during combat!", delete_after=5)
                return
            char = get_character(ctx.author.id)
            if char['current_floor'] >= char['deepest_floor']:
                await ctx.send("You're already at your deepest floor!", delete_after=5)
                return
            use_consumable(ctx.author.id, target_id)
            char['current_floor'] = char['deepest_floor']
            update_character(ctx.author.id, current_floor=char['current_floor'])
            await ctx.send(f"🔮 Warped to **Floor {char['current_floor']}**!")

    @commands.command(aliases=['inv'])
    async def inventory(self, ctx, *, flags: str = ""):
        """View your gear and items. Add 'show' to keep visible."""
        private = 'show' not in flags.lower()
        items = get_inventory(ctx.author.id)
        if not items:
            await ctx.send("Your inventory is empty. Go explore the dungeon!", delete_after=10)
            return
        embed = discord.Embed(title=f"🎒 {ctx.author.display_name}'s Inventory", color=0x3498DB)
        desc = ""
        for item in items[:25]:
            equipped = " ✅" if item['equipped'] else ""
            rarity_icon = RARITY_COLORS.get(item['rarity'], '⬜')
            desc += f"{rarity_icon} {item['emoji']} **{item['name']}** [{item['slot']}]{equipped}\n"
        embed.description = desc

        # Show consumables
        stock = get_consumables(ctx.author.id)
        if stock:
            cons_parts = []
            for cid, qty in stock.items():
                if cid in CONSUMABLES:
                    cons_parts.append(f"{CONSUMABLES[cid]['emoji']} {CONSUMABLES[cid]['name']} x{qty}")
            if cons_parts:
                embed.add_field(name="🧴 Consumables", value="\n".join(cons_parts), inline=False)

        embed.set_footer(text=f"Total: {len(items)} items | !equip <name> | !use <consumable>")
        await ctx.send(embed=embed)

    @commands.command()
    async def equip(self, ctx, *, item_name: str):
        """Equip an item by name"""
        item_id = None
        for iid, data in ITEMS.items():
            if data['name'].lower() == item_name.lower().strip():
                item_id = iid
                break
        if not item_id:
            await ctx.send("Item not found. Check `!inventory` for your items.", delete_after=10)
            return
        success, msg = equip_item(ctx.author.id, item_id)
        icon = "✅" if success else "❌"
        await ctx.send(f"{icon} {msg}")

    @commands.group(invoke_without_command=True)
    async def dungeon(self, ctx):
        pass

    @dungeon.command()
    async def top(self, ctx):
        """Dungeon Leaderboard"""
        db.cursor.execute("SELECT user_id, deepest_floor, current_floor FROM adventure_character ORDER BY deepest_floor DESC LIMIT 10")
        rows = db.cursor.fetchall()
        desc = ""
        for i, r in enumerate(rows, 1):
            user = self.bot.get_user(int(r['user_id']))
            name = user.display_name if user else f"User {r['user_id']}"
            desc += f"**{i}.** {name} — Max Floor: {r['deepest_floor']} (Current: {r['current_floor']})\n"
        embed = discord.Embed(title="🏆 Dungeon Leaderboard", description=desc or "No data.", color=0xF1C40F)
        await ctx.send(embed=embed)

    @commands.command(name="dungeon_guide", aliases=["dguide", "advguide"])
    async def dungeon_guide(self, ctx, *, flags: str = ""):
        """Complete guide to the Dungeon Adventure system. Add 'show' to keep visible."""

        # Page 1: Getting Started
        e1 = discord.Embed(title="🗡️ Dungeon Guide — Getting Started", color=0x3498DB)
        e1.description = (
            "The Dungeon is a text-based roguelike RPG. "
            "Explore floors, fight enemies, collect loot, and try not to die.\n\n"
            "**How to Start**\n"
            "Type `!adventure` (or `!adv`) to enter the dungeon.\n\n"
            "**The Main Menu**\n"
            "Once inside, you have 3 buttons:\n"
            "⚔️ **Explore** — Move to the next room (combat, treasure, trap, shrine, or empty)\n"
            "💊 **Rest** — Heal 30% of max HP for 50 🪙\n"
            "📊 **Stats** — View your full stats including gear bonuses\n\n"
            "**Room Types**\n"
            "```\n"
            "⚔️ Combat     (50%)  — Fight an enemy\n"
            "💰 Treasure   (20%)  — Free coins + possible loot\n"
            "⚠️ Trap       (15%)  — Take damage, might die\n"
            "🛕 Shrine     (10%)  — Free heal (15% HP)\n"
            "🕯️ Empty       (5%)  — Safe passage\n"
            "👑 Boss       (100%) — Every 10th floor\n"
            "```\n\n"
            "**Dungeon Leveling**\n"
            "Kill enemies to earn ⭐ XP. Level up for:\n"
            "❤️ +5 Max HP per level\n"
            "📈 +1 random stat (ATK/DEF/SPD/LCK)\n"
            "Boss kills grant 3× XP!"
        )
        e1.set_footer(text="Page 1/4 — Getting Started")

        # Page 2: Combat
        e2 = discord.Embed(title="⚔️ Dungeon Guide — Combat", color=0xE74C3C)
        e2.description = (
            "**Your Stats**\n"
            "```\n"
            "ATK — Determines damage dealt\n"
            "DEF — Reduces incoming damage\n"
            "SPD — Affects flee success rate\n"
            "LCK — Increases critical hit chance\n"
            "HP  — Don't let this reach 0\n"
            "```\n\n"
            "**Damage Formula**\n"
            "`base = max(1, ATK - enemy_DEF)`\n"
            "`damage = base × random(0.85–1.15) × (1 + damage_bonus)`\n"
            "`crit = 1.5× + crit_damage_bonus`\n\n"
            "**Combat Flow**\n"
            "1. Your special effects trigger (Starforger's, Red Dragon self-dmg)\n"
            "2. You attack → bleed/lifesteal applied\n"
            "3. Bleed ticks on enemy\n"
            "4. Enemy attacks you (magic dodge may block)\n\n"
            "**Death Penalty**\n"
            "💀 Lose **5 floors** (min floor 1)\n"
            "💸 15% wallet tax\n"
            "❤️ HP fully restored\n"
            "💀 **Revival Token** can prevent floor loss!\n\n"
            "**Bosses**\n"
            "Every 10th floor (10, 20, 30...) is a guaranteed boss fight.\n"
            "Bosses have 2.5× more HP and higher stats.\n"
            "Bosses are the **only source of Legendary drops**."
        )
        e2.set_footer(text="Page 2/4 — Combat")

        # Page 3: Items
        e3 = discord.Embed(title="🎒 Dungeon Guide — Items & Gear", color=0x9B59B6)
        e3.description = (
            "Items drop from combat wins and treasure rooms.\n"
            "Use `!inventory` to view and `!equip <name>` to equip.\n"
            "You can equip **one item per slot** (weapon / armor / accessory).\n\n"
            "**Item Tiers**\n"
            "```\n"
            "⬜ Common     Floors 1-15    60% weight\n"
            "🟩 Uncommon   Floors 5-30    30% weight\n"
            "🟦 Rare       Floors 10-50   15% weight\n"
            "🟪 Epic       Floors 20-80    5% weight\n"
            "🟧 Legendary  Floor 35+       1% (boss only)\n"
            "```\n\n"
            "**★ LEGENDARY GEAR ★**\n\n"
            "🗡️ **The Blackened Sword** (+30 ATK, +5 SPD)\n"
            "› 5% chance to gain +1 random stat on kill\n"
            "› 10% chance vs demon-type enemies\n\n"
            "⏳ **Hourglass of the Archiver** (+10 DEF, +15 LCK)\n"
            "› 30% chance to completely dodge enemy attacks\n\n"
            "🔱 **Spear of the Red Dragon** (+35 ATK)\n"
            "› +20% damage on all attacks\n"
            "› ⚠️ Costs 5% of your max HP every turn\n\n"
            "⭐ **Starforger's Favor** (+25 LCK, +10 ATK)\n"
            "› 3% chance per turn to instantly halve enemy HP\n\n"
            "🗡️ **Dagger and Pike** (+22 ATK, +10 SPD)\n"
            "› Applies 3 bleed stacks on hit\n"
            "› Heals 10% of damage dealt\n\n"
            "👻 **Banshee's Call** (+18 ATK, +8 SPD)\n"
            "› +50% crit chance, +50% crit damage\n\n"
            "👑 **Crown of the Endless** (+20 DEF, +15 LCK, +50 HP)\n"
            "› 15% magic dodge + 3% lifesteal"
        )
        e3.set_footer(text="Page 3/4 — Items & Gear")

        # Page 4: Strategy & Enemies
        e4 = discord.Embed(title="🧠 Dungeon Guide — Strategy & Enemies", color=0x2ECC71)
        e4.description = (
            "**Enemy Types**\n"
            "```\n"
            "🐾 Beast      — Cave Bat, Spider, Dire Wolf, Manticore\n"
            "💀 Undead      — Skeleton, Wraith, Lich, Death Knight\n"
            "👤 Humanoid    — Goblin, Bandit, Assassin, War Mage\n"
            "🧪 Slime       — Slime, Acid Blob, Crystal Ooze\n"
            "😈 Demon       — Shadow Fiend, Pit Fiend, Doom Herald\n"
            "🔥 Elemental   — Fire, Frost, Storm, Void, Chaos\n"
            "🐉 Dragon      — Drake, Wyvern, Elder Wyrm\n"
            "```\n"
            "Demons appear Floor 21+. Elementals Floor 51+. Dragons Floor 81+.\n\n"
            "**Floor Progression**\n"
            "```\n"
            "Floor   1-5   — Slimes, Bats, Goblins\n"
            "Floor   6-10  — Skeletons, Bandits, Wolves\n"
            "Floor  11-20  — Dark Knights, Wraiths\n"
            "Floor  21-35  — Shadow Fiends, Liches\n"
            "Floor  36-50  — Abyssal Knights, Assassins\n"
            "Floor  51-65  — Revenants, Elementals\n"
            "Floor  66-80  — Death Knights, Chimeras\n"
            "Floor  81-95  — Drakes, Phantoms, Hydras\n"
            "Floor  96-110 — Void/Chaos Elementals, Wyrms\n"
            "Floor  111+   — Shadow Dragons, Endgame\n"
            "```\n\n"
            "**Named Bosses**\n"
            "`F10` The Warden  •  `F20` Crimson Butcher\n"
            "`F30` Void Sentinel  •  `F40` Abyssal Overlord\n"
            "`F50` The Nameless King  •  `F60` Stormbreaker Titan\n"
            "`F70` The Undying Lich  •  `F80` Infernal Archon\n"
            "`F90` Wyrm of the Abyss  •  `F100` **The World Ender**\n\n"
            "**Tips**\n"
            "• **Rest before bosses** — Don't enter with low HP\n"
            "• **Bank your coins** — Death tax only hits wallet\n"
            "• **Dagger + Pike** is the safest legendary (sustain)\n"
            "• **Crown of the Endless** is the ultimate tank accessory"
        )
        e4.set_footer(text="Page 4/4 — Strategy & Enemies  •  Good luck in there.")

        await ctx.send(embeds=[e1, e2, e3, e4])


async def setup(bot):
    await bot.add_cog(Adventure(bot))
