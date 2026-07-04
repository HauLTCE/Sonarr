"""Dungeon UI views: the main menu, combat, and the post-timeout restart prompt.
Combat enforces a 3-item-per-battle cap (potions + combat consumables share it)."""
import discord
import random
import asyncio
from utils.economy_helpers import update_wallet, get_balance
from cogs.adventure.engine import (
    get_character, update_character, calculate_damage, process_bleed,
    apply_on_kill_stat, is_demon, award_dungeon_xp, calculate_xp_reward,
)
from cogs.adventure.items import (
    get_equipped_bonuses, give_item, get_item, roll_loot,
    RARITY_COLORS, RARITY_DISPLAY,
)
from utils.consumables import use_consumable, get_consumables, CONSUMABLES
from cogs.adventure.text_adapter import SimpleContext, send_to_log_channel

# Shared cap: total combat-item activations allowed per battle.
MAX_ITEMS_PER_BATTLE = 3


def hp_bar(current, maximum, length=10):
    """Visual HP bar; color shifts green→yellow→red as HP drops."""
    current = max(0, current)
    ratio = current / max(1, maximum)
    filled = round(ratio * length)
    if ratio > 0.6:
        ch = "🟩"
    elif ratio > 0.3:
        ch = "🟨"
    else:
        ch = "🟥"
    return ch * filled + "⬛" * (length - filled)


class RestartView(discord.ui.View):
    """Single Restart button, shown after a session times out."""
    def __init__(self, cog, user):
        super().__init__(timeout=None)
        self.cog = cog
        self.user = user

    @discord.ui.button(label="Restart Adventure", style=discord.ButtonStyle.success, emoji="🔄")
    async def restart(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.user.id:
            return
        self.cog._timeout_messages.pop(self.user.id, None)
        self.cog.active_adventures.add(self.user.id)
        for c in self.children:
            c.disabled = True
        await interaction.response.edit_message(content="🔄 Restarting...", view=self)
        await self.cog.show_main_menu(interaction.channel, SimpleContext(self.user))
        self.stop()


class _TimeoutMixin:
    """Shared on_timeout: delete the stale screen, post one restart prompt."""
    async def _handle_timeout(self, channel_attr="_message"):
        if getattr(self, "_left", False):
            return
        msg = getattr(self, "_message", None)
        ch = msg.channel if msg else None
        try:
            if msg:
                await msg.delete()
        except Exception:
            pass
        if not ch:
            return
        old = self.cog._timeout_messages.pop(self.ctx.author.id, None)
        if old:
            try:
                await old.delete()
            except Exception:
                pass
        view = RestartView(self.cog, self.ctx.author)
        prompt = await ch.send(
            f"⏰ {self.ctx.author.mention} Your dungeon session timed out.\n"
            f"Click below to restart or type `!adventure`.",
            view=view,
        )
        self.cog._timeout_messages[self.ctx.author.id] = prompt


class AdventureView(_TimeoutMixin, discord.ui.View):
    """Main dungeon menu shown between rooms."""
    def __init__(self, cog, ctx, char):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.char = char
        self._message = None
        self._left = False

    async def on_timeout(self):
        self.cog.active_adventures.discard(self.ctx.author.id)
        await self._handle_timeout()

    def _owned(self, interaction):
        return interaction.user.id == self.ctx.author.id

    @discord.ui.button(label="Explore", style=discord.ButtonStyle.danger, emoji="⚔️")
    async def explore(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._owned(interaction):
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        # M5: re-read from the DB so a prior text command (!rest, !use) that
        # mutated the character isn't clobbered by this view's stale snapshot.
        self.char = get_character(self.ctx.author.id)
        await self.cog.explore_room(interaction, self.char)

    @discord.ui.button(label="Rest", style=discord.ButtonStyle.success, emoji="💊")
    async def rest(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._owned(interaction):
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        # M5: act on fresh state, not the snapshot captured when the menu opened.
        self.char = get_character(self.ctx.author.id)
        msg = self.cog.do_rest(self.ctx.author.id, self.char)
        if msg.startswith("❌"):
            await interaction.response.send_message(msg, ephemeral=True)
            return
        bonuses = get_equipped_bonuses(self.ctx.author.id)
        eff_hp = self.char['max_hp'] + bonuses['max_hp']
        bal = get_balance(self.ctx.author.id)
        embed = discord.Embed(title="💊 Rested", color=0x2ECC71)
        embed.description = (
            f"{hp_bar(self.char['hp'], eff_hp)} {self.char['hp']}/{eff_hp}\n{msg}\n\n"
            f"Floor: {self.char['current_floor']}  |  Coins: {bal['wallet']:,} 🪙"
        )
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Stats", style=discord.ButtonStyle.secondary, emoji="📊")
    async def stats(self, interaction: discord.Interaction, button: discord.ui.Button):
        if not self._owned(interaction):
            await interaction.response.send_message("Not your adventure.", ephemeral=True)
            return
        self.char = get_character(self.ctx.author.id)
        await interaction.response.send_message(self.cog.build_stats_text(self.ctx.author.id, self.char), ephemeral=True)


class CombatView(_TimeoutMixin, discord.ui.View):
    """Turn-based combat. Attack / Flee / Potion buttons, plus text commands.
    items_used counts every combat-item activation (potions, scrolls, tonics);
    capped at MAX_ITEMS_PER_BATTLE to stop spam."""
    def __init__(self, cog, ctx, char, enemy, bonuses):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.char = char
        self.enemy = enemy
        self.bonuses = bonuses
        self.turn = 0
        self.combat_log = []
        self.items_used = 0
        self.shield_active = False
        self._message = None
        self._left = False
        # Re-entry guard (M6): one action (attack/flee/potion) resolves at a time.
        # Set synchronously before any await so a rapid second click — or a
        # button + text `!attack` interleave — can't both kill the enemy and run
        # handle_victory twice (double rewards). `_left` marks the combat done.
        self._busy = False

    async def on_timeout(self):
        self.cog.active_combats.pop(self.ctx.author.id, None)
        self.cog.active_adventures.discard(self.ctx.author.id)
        await self._handle_timeout()

    def items_remaining(self):
        return MAX_ITEMS_PER_BATTLE - self.items_used

    def get_embed_color(self):
        ratio = self.char['hp'] / max(1, self.char['max_hp'])
        return 0x2ECC71 if ratio > 0.6 else (0xF1C40F if ratio > 0.3 else 0xE74C3C)

    def type_emoji(self):
        t = self.enemy.get('type', 'humanoid')
        return {"beast": "🐾", "undead": "💀", "humanoid": "👤",
                "slime": "🧪", "demon": "😈", "elemental": "🔥",
                "dragon": "🐉"}.get(t, "❓")

    def update_embed(self, embed):
        self.turn += 1
        is_boss = self.enemy.get('is_boss', False)
        floor = self.char['current_floor']
        eff_hp = self.char['max_hp'] + self.bonuses.get('max_hp', 0)

        next_boss = ((floor // 10) + 1) * 10
        boss_str = "👑 **BOSS FIGHT**" if is_boss else f"Boss in {next_boss - floor} floors"

        e_hp = max(0, self.enemy['hp'])
        e_bar = hp_bar(e_hp, self.enemy['max_hp'])
        bleed = f"  🩸×{self.enemy['bleed_stacks']}" if self.enemy.get('bleed_stacks', 0) > 0 else ""
        p_bar = hp_bar(self.char['hp'], eff_hp)
        flee = min(0.95, (self.char['base_speed'] + self.bonuses['speed']) / max(1, self.enemy['speed']) * 0.6)

        embed.color = self.get_embed_color()
        embed.title = f"{'👑 BOSS' if is_boss else '⚔️ COMBAT'} — Floor {floor}"
        desc = (
            f"**{self.enemy['name']}** {self.type_emoji()} Lv.{self.enemy['level']}\n"
            f"{e_bar} {e_hp}/{self.enemy['max_hp']}{bleed}\n"
            f"`ATK {self.enemy['attack']}  DEF {self.enemy['defense']}  SPD {self.enemy['speed']}`\n\n"
            f"{self.ctx.author.mention} ⚔️\n"
            f"{p_bar} {max(0, self.char['hp'])}/{eff_hp}\n"
        )
        effects = []
        b = self.bonuses
        if b.get('lifesteal', 0) > 0: effects.append(f"🩸{b['lifesteal']*100:.0f}%")
        if b.get('magic_dodge', 0) > 0: effects.append(f"⏳{b['magic_dodge']*100:.0f}%")
        if b.get('crit_chance_bonus', 0) > 0: effects.append(f"👻+{b['crit_chance_bonus']*100:.0f}%crit")
        if b.get('execute_chance', 0) > 0: effects.append(f"⭐{b['execute_chance']*100:.0f}%exec")
        if b.get('bleed_on_hit', 0) > 0: effects.append("🗡️bleed")
        if b.get('damage_bonus', 0) > 0: effects.append(f"🔱+{b['damage_bonus']*100:.0f}%dmg")
        if effects:
            desc += f"`{'  '.join(effects)}`\n"
        if self.combat_log:
            desc += "\n**— Combat Log —**\n" + "\n".join(self.combat_log[-4:]) + "\n"
        desc += f"\n`Turn {self.turn}`  •  `🏃 Flee: {flee*100:.0f}%`  •  `{boss_str}`"
        embed.description = desc
        embed.set_footer(text=f"⚔️ !attack | 🏃 !flee | 🧪 Items: {self.items_used}/{MAX_ITEMS_PER_BATTLE} used")

    async def do_player_attack(self, interaction):
        # M6 re-entry guard: bail if the combat is already resolved or another
        # action is mid-flight. Check-and-set is synchronous (no await between),
        # so it's atomic against a concurrent click that resumes at the defer.
        if self._left or self._busy:
            try:
                await interaction.response.defer()
            except Exception:
                pass
            return
        self._busy = True
        try:
            await self._do_player_attack_inner(interaction)
        finally:
            self._busy = False

    async def _do_player_attack_inner(self, interaction):
        await interaction.response.defer()
        b = self.bonuses
        if b['execute_chance'] > 0 and random.random() < b['execute_chance']:
            halved = self.enemy['hp'] // 2
            self.enemy['hp'] -= halved
            self.combat_log.append(f"⭐ **Starforger's Favor!** Enemy HP halved (-{halved})!")
        if b['self_damage_pct'] > 0:
            self_dmg = max(1, int(self.char['max_hp'] * b['self_damage_pct']))
            self.char['hp'] -= self_dmg
            update_character(self.ctx.author.id, hp=self.char['hp'])
            self.combat_log.append(f"🔱 The Spear burns you for {self_dmg}.")

        total_atk = self.char['base_attack'] + b['attack']
        total_luck = self.char['base_luck'] + b['luck']
        dmg, is_crit = calculate_damage(
            total_atk, self.enemy['defense'], total_luck,
            crit_chance_bonus=b['crit_chance_bonus'],
            crit_damage_bonus=b['crit_damage_bonus'],
            damage_bonus=b['damage_bonus'],
        )
        self.enemy['hp'] -= dmg
        self.combat_log.append(f"⚔️ You deal **{dmg}**{' 💥**CRIT**' if is_crit else ''}")

        if b['bleed_on_hit'] > 0:
            self.enemy['bleed_stacks'] = self.enemy.get('bleed_stacks', 0) + b['bleed_on_hit']
            self.combat_log.append(f"🩸 +{b['bleed_on_hit']} bleed stacks")
        if b['lifesteal'] > 0:
            heal = max(1, int(dmg * b['lifesteal']))
            eff_hp = self.char['max_hp'] + b.get('max_hp', 0)
            self.char['hp'] = min(eff_hp, self.char['hp'] + heal)
            update_character(self.ctx.author.id, hp=self.char['hp'])
            self.combat_log.append(f"🩸 Healed {heal} HP")
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
        if is_boss:
            reward = int(reward * 1.5)
        update_wallet(self.ctx.author.id, reward)

        stat_msg = apply_on_kill_stat(
            self.ctx.author.id, self.char,
            is_demon(self.enemy.get('name', '').replace('👑 ', '')), self.bonuses,
        )
        self.char['current_floor'] += 1
        if self.char['current_floor'] > self.char['deepest_floor']:
            self.char['deepest_floor'] = self.char['current_floor']
        if is_boss:
            self.char['bosses_killed'] = self.char.get('bosses_killed', 0) + 1
            update_character(self.ctx.author.id, bosses_killed=self.char['bosses_killed'])
        update_character(self.ctx.author.id, current_floor=self.char['current_floor'], deepest_floor=self.char['deepest_floor'])

        loot_lines = await self._roll_and_report_loot(interaction, floor, is_boss)

        embed = discord.Embed(title=f"🎉 {'BOSS ' if is_boss else ''}VICTORY — Floor {floor}", color=0x2ECC71)
        xp_earned = calculate_xp_reward(self.enemy, floor, is_boss=is_boss)
        level_up_msg = award_dungeon_xp(self.ctx.author.id, self.char, xp_earned)
        lines = [
            f"{self.ctx.author.mention} — **{self.enemy['name']}** defeated!\n",
            f"💰 **+{reward:,} 🪙**  •  ⭐ **+{xp_earned} XP**  •  Floor {self.char['current_floor']}",
        ]
        if level_up_msg: lines.append(f"\n{level_up_msg}")
        if stat_msg: lines.append(f"📈 {stat_msg}")
        if loot_lines:
            lines.append("\n**— Loot —**")
            lines.extend(loot_lines)
        else:
            lines.append("*No loot this time.*")
        embed.description = "\n".join(lines)
        for c in self.children: c.disabled = True
        self._left = True
        await interaction.edit_original_response(embed=embed, view=self)

        guild = interaction.guild
        for milestone in (10, 25, 50):
            if self.char['deepest_floor'] == milestone + 1 and guild:
                await send_to_log_channel(self.cog.bot, guild,
                    f"🏔️ **{self.ctx.author.display_name}** cleared Floor {milestone}!")
        await asyncio.sleep(2)
        self.cog.active_combats.pop(self.ctx.author.id, None)
        await self.cog.show_main_menu(interaction.message.channel, self.ctx, self.char)
        self.stop()

    async def _roll_and_report_loot(self, interaction, floor, is_boss):
        loot_id = roll_loot(floor, is_boss=is_boss)
        if not loot_id:
            return []
        _, auto = give_item(self.ctx.author.id, loot_id)
        data = get_item(loot_id)
        icon = RARITY_COLORS.get(data['rarity'], '⬜')
        equip_str = " *(auto-equipped)*" if auto else ""
        lines = [f"{icon} {data['emoji']} **{data['name']}** ({RARITY_DISPLAY[data['rarity']]}){equip_str}"]
        parts = [f"{'+' if v > 0 else ''}{v} {s.upper()}" for s, v in data.get('stats', {}).items()]
        if parts:
            lines.append(f"  `{' / '.join(parts)}`")
        if data['rarity'] == 'legendary' and interaction.guild:
            await send_to_log_channel(self.cog.bot, interaction.guild,
                f"⭐ **{self.ctx.author.display_name}** obtained **{data['name']}** on Floor {floor}!")
        return lines

    async def enemy_turn(self, interaction):
        if self.shield_active:
            self.shield_active = False
            self.combat_log.append("🛡️ **Shield absorbed the attack!**")
        elif self.bonuses['magic_dodge'] > 0 and random.random() < self.bonuses['magic_dodge']:
            self.combat_log.append("⏳ Hourglass shimmers — attack dodged!")
        else:
            total_def = self.char['base_defense'] + self.bonuses['defense']
            dmg, is_crit = calculate_damage(self.enemy['attack'], total_def)
            self.char['hp'] -= dmg
            self.combat_log.append(f"🔻 {self.enemy['name']} deals **{dmg}**{' 💥**CRIT**' if is_crit else ''}")
        update_character(self.ctx.author.id, hp=self.char['hp'])

        try:
            embed = interaction.message.embeds[0]
        except (AttributeError, IndexError, TypeError):
            embed = discord.Embed()
        self.update_embed(embed)

        if self.char['hp'] <= 0:
            await self._handle_death(interaction)
        else:
            await interaction.edit_original_response(embed=embed, view=self)

    async def _handle_death(self, interaction):
        self.char['total_deaths'] += 1
        self.char['hp'] = self.char['max_hp']
        old_floor = self.char['current_floor']
        has_revival = use_consumable(self.ctx.author.id, "revival_token")
        floor_penalty = 0 if has_revival else 5
        self.char['current_floor'] = old_floor if has_revival else max(1, old_floor - floor_penalty)
        update_character(self.ctx.author.id, hp=self.char['hp'],
                         current_floor=self.char['current_floor'], total_deaths=self.char['total_deaths'])
        bal = get_balance(self.ctx.author.id)
        tax = int(bal['wallet'] * 0.15)
        update_wallet(self.ctx.author.id, -tax)

        embed = discord.Embed(title="💀 YOU DIED", color=0x000000)
        if has_revival:
            embed.description = (
                f"{self.ctx.author.mention} — **{self.enemy['name']}** killed you on Floor {old_floor}.\n\n"
                f"💀 **Revival Token** consumed! Floor loss prevented.\n"
                f"```\nFloor:   {old_floor} (saved)\nTax:     -{tax:,} 🪙  (15% wallet)\nHP:      Fully restored\n```\n"
                f"*Type `!adventure` to try again.*")
        else:
            embed.description = (
                f"{self.ctx.author.mention} — **{self.enemy['name']}** killed you on Floor {old_floor}.\n\n"
                f"```\nFloor:   {old_floor} → {self.char['current_floor']}  (-{floor_penalty})\n"
                f"Tax:     -{tax:,} 🪙  (15% wallet)\nHP:      Fully restored\nDeaths:  {self.char['total_deaths']} total\n```\n"
                f"*Type `!adventure` to try again.*")
        for c in self.children: c.disabled = True
        self._left = True
        await interaction.edit_original_response(embed=embed, view=self)
        if interaction.guild:
            await send_to_log_channel(self.cog.bot, interaction.guild,
                f"💀 **{self.ctx.author.display_name}** was slain by {self.enemy['name']} on Floor {old_floor}.")
        self.cog.active_combats.pop(self.ctx.author.id, None)
        self.cog.active_adventures.discard(self.ctx.author.id)
        self.stop()

    @discord.ui.button(label="Attack", style=discord.ButtonStyle.danger, emoji="⚔️")
    async def attack(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your battle.", ephemeral=True)
            return
        await self.do_player_attack(interaction)

    async def do_flee(self, interaction):
        if self._left or self._busy:
            try:
                await interaction.response.defer()
            except Exception:
                pass
            return
        self._busy = True
        try:
            await self._do_flee_inner(interaction)
        finally:
            self._busy = False

    async def _do_flee_inner(self, interaction):
        await interaction.response.defer()
        flee = min(0.95, (self.char['base_speed'] + self.bonuses['speed']) / max(1, self.enemy['speed']) * 0.6)
        if random.random() < flee:
            embed = discord.Embed(title="🏃 Fled!", color=0xF1C40F)
            embed.description = f"{self.ctx.author.mention} escaped from **{self.enemy['name']}**."
            for c in self.children: c.disabled = True
            self._left = True
            await interaction.edit_original_response(embed=embed, view=self)
            await asyncio.sleep(2)
            self.cog.active_combats.pop(self.ctx.author.id, None)
            await self.cog.show_main_menu(interaction.message.channel, self.ctx, self.char)
            self.stop()
        else:
            self.combat_log.append(f"🏃 Flee failed! ({flee*100:.0f}% chance)")
            await self.enemy_turn(interaction)

    @discord.ui.button(label="Flee", style=discord.ButtonStyle.secondary, emoji="🏃")
    async def flee(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            return
        await self.do_flee(interaction)

    async def do_use_potion(self, interaction, potion_type="health_potion"):
        if self._left or self._busy:
            try:
                await interaction.response.defer()
            except Exception:
                pass
            return
        self._busy = True
        try:
            await self._do_use_potion_inner(interaction, potion_type)
        finally:
            self._busy = False

    async def _do_use_potion_inner(self, interaction, potion_type):
        if self.items_used >= MAX_ITEMS_PER_BATTLE:
            await interaction.response.send_message(
                f"🧪 Item limit reached! ({MAX_ITEMS_PER_BATTLE}/{MAX_ITEMS_PER_BATTLE} used this battle)", ephemeral=True)
            return
        stock = get_consumables(self.ctx.author.id)
        if stock.get(potion_type, 0) <= 0:
            await interaction.response.send_message(
                f"You don't have any **{CONSUMABLES[potion_type]['name']}**. Buy from `!shop consumables`.", ephemeral=True)
            return
        eff_hp = self.char['max_hp'] + self.bonuses.get('max_hp', 0)
        if self.char['hp'] >= eff_hp:
            await interaction.response.send_message("You're already at full HP!", ephemeral=True)
            return
        await interaction.response.defer()
        # Only heal if a potion was actually consumed (use_consumable is atomic).
        if not use_consumable(self.ctx.author.id, potion_type):
            await interaction.followup.send(
                f"You don't have any **{CONSUMABLES[potion_type]['name']}** left.", ephemeral=True)
            return
        self.items_used += 1
        old_hp = self.char['hp']
        self.char['hp'] = eff_hp if potion_type == "greater_potion" else min(eff_hp, self.char['hp'] + 50)
        update_character(self.ctx.author.id, hp=self.char['hp'])
        name = CONSUMABLES[potion_type]['name']
        self.combat_log.append(f"🧪 **{name}** — +{self.char['hp'] - old_hp} HP ({self.items_used}/{MAX_ITEMS_PER_BATTLE})")
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
