"""Text-based dungeon RPG. The cog is intentionally thin: combat/menu UI lives in
views.py, consumable logic in consumables_logic.py, mechanics in engine.py, and
gear in items.py. This file wires commands to those pieces."""
import discord
from discord.ext import commands
import random
import asyncio
from utils.economy_helpers import update_wallet, get_balance
from utils.database import db
from cogs.adventure.engine import (
    get_character, update_character, generate_enemy, generate_boss,
    xp_for_next_level,
)
from cogs.adventure.items import (
    get_equipped_bonuses, get_equipped_items, get_inventory, equip_item,
    give_item, roll_loot, get_item, ITEMS, RARITY_COLORS, ALL_SLOTS,
)
from utils.consumables import CONSUMABLES, get_consumables
from cogs.adventure.text_adapter import SimpleContext, TextInteraction
from cogs.adventure.views import AdventureView, CombatView, hp_bar
from cogs.adventure.consumables_logic import handle_use
from cogs.adventure.guide import build_guide_embeds, build_guide_embeds_2

CMD_HINT = "💡 !inventory | !equip | !use | !shop | !leave"


class Adventure(commands.Cog):
    """Text-based RPG Adventure."""
    def __init__(self, bot):
        self.bot = bot
        self.active_adventures = set()
        self.active_combats = {}       # user_id -> CombatView
        self._timeout_messages = {}    # user_id -> Message (latest restart prompt)
        self._adventure_views = {}     # user_id -> AdventureView (for marking _left)

    async def cog_check(self, ctx):
        from cogs.games.utils import check_channel
        return await check_channel(ctx, "dungeon_channel")

    # ── Shared helpers ──
    def do_rest(self, user_id, char):
        """Heal 30% max HP for 50 coins. Returns a status line ('❌...' on failure)."""
        bal = get_balance(user_id)
        if bal['wallet'] < 50:
            return "❌ You need 50 🪙 to rest."
        update_wallet(user_id, -50)
        bonuses = get_equipped_bonuses(user_id)
        eff_hp = char['max_hp'] + bonuses['max_hp']
        old = char['hp']
        char['hp'] = min(eff_hp, char['hp'] + int(eff_hp * 0.3))
        update_character(user_id, hp=char['hp'])
        return f"Healed **{char['hp'] - old} HP** (-50 🪙)"

    def build_stats_text(self, user_id, char):
        bonuses = get_equipped_bonuses(user_id)
        def fmt(base, bonus):
            return f"{base}" + (f" (+{bonus})" if bonus > 0 else (f" ({bonus})" if bonus < 0 else ""))
        txt = (
            f"**Floor:** {char['current_floor']}  |  **Max:** {char['deepest_floor']}\n"
            f"**HP:** {char['hp']}/{char['max_hp'] + bonuses['max_hp']}\n"
            f"**ATK:** {fmt(char['base_attack'], bonuses['attack'])}\n"
            f"**DEF:** {fmt(char['base_defense'], bonuses['defense'])}\n"
            f"**SPD:** {fmt(char['base_speed'], bonuses['speed'])}\n"
            f"**LCK:** {fmt(char['base_luck'], bonuses['luck'])}\n"
            f"**Bosses Killed:** {char['bosses_killed']}\n"
            f"**Deaths:** {char['total_deaths']}"
        )
        if bonuses['lifesteal'] > 0: txt += f"\n🩸 Lifesteal: {bonuses['lifesteal']*100:.0f}%"
        if bonuses['magic_dodge'] > 0: txt += f"\n⏳ Magic Dodge: {bonuses['magic_dodge']*100:.0f}%"
        if bonuses['crit_chance_bonus'] > 0: txt += f"\n👻 Crit Bonus: +{bonuses['crit_chance_bonus']*100:.0f}%"
        txt += (
            "\n\n**— What Stats Do —**\n```\n"
            "⚔️ ATK — Damage dealt to enemies\n"
            "🛡️ DEF — Reduces incoming damage\n"
            "💨 SPD — Flee success rate\n"
            "🍀 LCK — Critical hit chance\n"
            "❤️ HP  — Health. 0 = death.\n```"
        )
        return txt

    # ── Main menu ──
    async def show_main_menu(self, channel, ctx, char=None):
        old_adv = self._adventure_views.pop(ctx.author.id, None)
        if old_adv:
            old_adv._left = True
            old_adv.stop()
        if not char:
            char = get_character(ctx.author.id)
        bal = get_balance(ctx.author.id)
        bonuses = get_equipped_bonuses(ctx.author.id)
        eff_hp = char['max_hp'] + bonuses['max_hp']
        floor = char['current_floor']

        next_boss = ((floor // 10) + 1) * 10
        floors_to_boss = next_boss - floor
        boss_str = "👑 **BOSS NEXT!**" if floors_to_boss == 0 else f"Boss in {floors_to_boss} floors"

        hp_ratio = char['hp'] / max(1, eff_hp)
        hp_warning = ""
        if hp_ratio <= 0.4:
            stock = get_consumables(ctx.author.id)
            has_potion = stock.get('health_potion', 0) > 0 or stock.get('greater_potion', 0) > 0
            hp_warning = ("\n⚠️ **LOW HP** — You have potions! Use `!use health potion`"
                          if has_potion else "\n⚠️ **LOW HP** — Rest or buy potions at `!shop`")

        equipped = get_equipped_items(ctx.author.id)
        gear_parts = []
        for slot in ALL_SLOTS:
            if slot in equipped:
                it = equipped[slot]
                gear_parts.append(f"{RARITY_COLORS.get(it['rarity'], '⬜')} {it['emoji']} {it['name']}")
            else:
                gear_parts.append(f"⬛ *{slot}*")
        gear_str = "  |  ".join(gear_parts)

        d_level = char.get('dungeon_level', 1)
        d_xp = char.get('dungeon_xp', 0)
        d_xp_next = xp_for_next_level(d_level)
        xp_filled = round(d_xp / max(1, d_xp_next) * 10)
        xp_bar = "🟦" * xp_filled + "⬛" * (10 - xp_filled)

        embed = discord.Embed(title=f"🗡️ {ctx.author.display_name}'s Dungeon",
                              color=0x3498DB if hp_ratio > 0.3 else 0xE74C3C)
        embed.description = (
            f"{ctx.author.mention} — Floor {floor}\n"
            f"{hp_bar(char['hp'], eff_hp)} {char['hp']}/{eff_hp} HP  |  {bal['wallet']:,} 🪙\n"
            f"⭐ **Lv.{d_level}**  {xp_bar} {d_xp}/{d_xp_next} XP\n\n"
            f"⚔️ `ATK {char['base_attack']+bonuses['attack']}`  "
            f"🛡️ `DEF {char['base_defense']+bonuses['defense']}`  "
            f"💨 `SPD {char['base_speed']+bonuses['speed']}`  "
            f"🍀 `LCK {char['base_luck']+bonuses['luck']}`\n\n"
            f"**Gear:** {gear_str}\n\n"
            f"{boss_str}  •  Best: Floor {char['deepest_floor']}{hp_warning}"
        )
        embed.set_footer(text=CMD_HINT)
        view = AdventureView(self, ctx, char)
        msg = await channel.send(embed=embed, view=view)
        view._message = msg
        self._adventure_views[ctx.author.id] = view

    # ── Explore a room ──
    async def explore_room(self, interaction, char):
        await interaction.response.defer()
        old_adv = self._adventure_views.pop(interaction.user.id, None)
        if old_adv:
            old_adv._left = True
            old_adv.stop()

        roll = random.random()
        floor = char['current_floor']
        bonuses = get_equipped_bonuses(interaction.user.id)
        is_boss_floor = floor % 10 == 0 and floor > 0

        if is_boss_floor or roll < 0.50:
            old_combat = self.active_combats.pop(interaction.user.id, None)
            if old_combat:
                old_combat._left = True
                old_combat.stop()
            enemy = generate_boss(floor) if is_boss_floor else generate_enemy(floor)
            embed = discord.Embed(
                title=f"{'👑 BOSS' if is_boss_floor else '⚔️ COMBAT'} — Floor {floor}",
                color=0xFF0000 if is_boss_floor else 0xE74C3C)
            view = CombatView(self, SimpleContext(interaction.user), char, enemy, bonuses)
            view.update_embed(embed)
            self.active_combats[interaction.user.id] = view
            await interaction.edit_original_response(embed=embed, view=view)
            try:
                view._message = await interaction.original_response()
            except Exception:
                view._message = getattr(interaction, "message", None)
            return

        if roll < 0.70:
            await self._room_treasure(interaction, char, floor)
        elif roll < 0.85:
            await self._room_trap(interaction, char, floor, bonuses)
        elif roll < 0.95:
            await self._room_shrine(interaction, char, bonuses)
        else:
            await self._room_empty(interaction, char)

    def _advance(self, user_id, char):
        char['current_floor'] += 1
        if char['current_floor'] > char['deepest_floor']:
            char['deepest_floor'] = char['current_floor']
        update_character(user_id, current_floor=char['current_floor'], deepest_floor=char['deepest_floor'])

    async def _room_treasure(self, interaction, char, floor):
        coins = random.randint(20, 80) * floor
        update_wallet(interaction.user.id, coins)
        loot_id = roll_loot(floor)
        loot_msg = ""
        if loot_id:
            _, auto = give_item(interaction.user.id, loot_id)
            data = get_item(loot_id)
            icon = RARITY_COLORS.get(data['rarity'], '⬜')
            equip_str = " *(auto-equipped)*" if auto else ""
            parts = [f"{'+' if v > 0 else ''}{v} {k.upper()}" for k, v in data.get('stats', {}).items()]
            stat_str = f"\n  `{' / '.join(parts)}`" if parts else ""
            loot_msg = f"\n{icon} {data['emoji']} **{data['name']}**{equip_str}{stat_str}"
        embed = discord.Embed(title="💰 Treasure Room!", color=0xF1C40F)
        embed.description = f"You found an ancient chest. Inside was {coins:,} 🪙!{loot_msg}\n\n*(Advancing...)*"
        self._advance(interaction.user.id, char)
        await interaction.edit_original_response(embed=embed, view=None)
        await asyncio.sleep(3)
        await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

    async def _room_shrine(self, interaction, char, bonuses):
        eff_hp = char['max_hp'] + bonuses.get('max_hp', 0)
        old = char['hp']
        char['hp'] = min(eff_hp, char['hp'] + int(eff_hp * 0.15))
        self._advance(interaction.user.id, char)
        update_character(interaction.user.id, hp=char['hp'])
        embed = discord.Embed(title="🛕 Rest Shrine", color=0x2ECC71)
        embed.description = (
            f"{interaction.user.mention}\nYou found a glowing shrine. Its warmth heals your wounds.\n\n"
            f"{hp_bar(char['hp'], eff_hp)} {char['hp']}/{eff_hp} HP  (+{char['hp'] - old})\n\n*(Advancing...)*")
        await interaction.edit_original_response(embed=embed, view=None)
        await asyncio.sleep(3)
        await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

    async def _room_empty(self, interaction, char):
        embed = discord.Embed(title="🕯️ Empty Room", color=0x95A5A6)
        embed.description = "The room is empty. You move on safely.\n\n*(Advancing...)*"
        self._advance(interaction.user.id, char)
        await interaction.edit_original_response(embed=embed, view=None)
        await asyncio.sleep(2)
        await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)

    async def _room_trap(self, interaction, char, floor, bonuses):
        dmg = random.randint(10, 20) + floor
        char['hp'] -= dmg
        eff_hp = char['max_hp'] + bonuses.get('max_hp', 0)
        if char['hp'] > 0:
            update_character(interaction.user.id, hp=char['hp'])
            embed = discord.Embed(title="⚠️ TRAP!", color=0xE74C3C)
            embed.description = (
                f"A hidden trap dealt **{dmg} damage**!\n\n"
                f"{hp_bar(char['hp'], eff_hp)} {char['hp']}/{eff_hp} HP\n\n*(Advancing...)*")
            await interaction.edit_original_response(embed=embed, view=None)
            await asyncio.sleep(3)
            await self.show_main_menu(interaction.message.channel, SimpleContext(interaction.user), char)
            return

        # Death by trap
        from utils.consumables import use_consumable
        char['total_deaths'] += 1
        char['hp'] = char['max_hp']
        old_floor = char['current_floor']
        has_revival = use_consumable(interaction.user.id, "revival_token")
        penalty = 0 if has_revival else 5
        char['current_floor'] = old_floor if has_revival else max(1, old_floor - penalty)
        update_character(interaction.user.id, hp=char['hp'],
                         current_floor=char['current_floor'], total_deaths=char['total_deaths'])
        bal = get_balance(interaction.user.id)
        tax = int(bal['wallet'] * 0.15)
        update_wallet(interaction.user.id, -tax)
        embed = discord.Embed(title="💀 YOU DIED", color=0x000000)
        if has_revival:
            embed.description = (
                f"{interaction.user.mention} — A hidden trap dealt **{dmg} damage** and killed you.\n\n"
                f"💀 **Revival Token** consumed! Floor loss prevented.\n"
                f"```\nFloor:   {old_floor} (saved)\nTax:     -{tax:,} 🪙  (15% wallet)\nHP:      Fully restored\n```\n"
                f"*Type `!adventure` to try again.*")
        else:
            embed.description = (
                f"{interaction.user.mention} — A hidden trap dealt **{dmg} damage** and killed you.\n\n"
                f"```\nFloor:   {old_floor} → {char['current_floor']}  (-{penalty})\n"
                f"Tax:     -{tax:,} 🪙  (15% wallet)\nHP:      Fully restored\nDeaths:  {char['total_deaths']} total\n```\n"
                f"*Type `!adventure` to try again.*")
        await interaction.edit_original_response(embed=embed, view=None)
        self.active_adventures.discard(interaction.user.id)

    # ── Commands ──
    @commands.command(aliases=['adv'])
    async def adventure(self, ctx):
        """Enter the dungeon or re-open the menu if stuck."""
        if ctx.author.id in self.active_adventures:
            self.active_combats.pop(ctx.author.id, None)
            self.active_adventures.discard(ctx.author.id)
        self.active_adventures.add(ctx.author.id)
        await self.show_main_menu(ctx.channel, ctx)

    @commands.command(aliases=['quit', 'exitdungeon'])
    async def leave(self, ctx):
        """Leave the dungeon and unlock yourself."""
        was_active = ctx.author.id in self.active_adventures
        self.active_adventures.discard(ctx.author.id)
        for store in (self._adventure_views, self.active_combats):
            v = store.pop(ctx.author.id, None)
            if v:
                v._left = True
                v.stop()
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

    @commands.command()
    async def explore(self, ctx):
        """Explore the next room (text alternative to the Explore button)."""
        if ctx.author.id in self.active_combats:
            await ctx.send("⚔️ You're in combat! Use `!attack` or `!flee`.", delete_after=5)
            return
        self.active_adventures.add(ctx.author.id)
        char = get_character(ctx.author.id)
        screen = await ctx.send("🚪 Opening the next door...")
        await self.explore_room(TextInteraction(self, ctx, screen), char)

    @commands.command(aliases=['atk', 'hit'])
    async def attack(self, ctx):
        """Attack the current enemy (text alternative to the Attack button)."""
        combat = self.active_combats.get(ctx.author.id)
        if not combat:
            await ctx.send("⚔️ Not in combat. Use `!explore` first.", delete_after=5)
            return
        await combat.do_player_attack(TextInteraction(self, ctx, combat._message))

    @commands.command(aliases=['run'])
    async def flee(self, ctx):
        """Flee from current combat (text alternative to the Flee button)."""
        combat = self.active_combats.get(ctx.author.id)
        if not combat:
            await ctx.send("You're not in combat.", delete_after=5)
            return
        await combat.do_flee(TextInteraction(self, ctx, combat._message))

    @commands.command()
    async def rest(self, ctx):
        """Heal 30% HP for 50 coins (text alternative to the Rest button)."""
        if ctx.author.id in self.active_combats:
            await ctx.send("❌ Can't rest during combat!", delete_after=5)
            return
        char = get_character(ctx.author.id)
        msg = self.do_rest(ctx.author.id, char)
        if msg.startswith("❌"):
            await ctx.send(msg, delete_after=5)
            return
        bonuses = get_equipped_bonuses(ctx.author.id)
        eff_hp = char['max_hp'] + bonuses['max_hp']
        bal = get_balance(ctx.author.id)
        embed = discord.Embed(title="💊 Rested", color=0x2ECC71)
        embed.description = f"{ctx.author.mention}\n{hp_bar(char['hp'], eff_hp)} {char['hp']}/{eff_hp}\n{msg}  |  {bal['wallet']:,} 🪙"
        await ctx.send(embed=embed)

    @commands.command()
    async def use(self, ctx, *, item_name: str):
        """Use a consumable item from your inventory."""
        await handle_use(self, ctx, item_name)

    @commands.command(aliases=['inv'])
    async def inventory(self, ctx, *, flags: str = ""):
        """View your gear and items."""
        items = get_inventory(ctx.author.id)
        if not items:
            await ctx.send("Your inventory is empty. Go explore the dungeon!", delete_after=10)
            return
        embed = discord.Embed(title=f"🎒 {ctx.author.display_name}'s Inventory", color=0x3498DB)
        desc = ""
        for item in items[:25]:
            eq = " ✅" if item['equipped'] else ""
            desc += f"{RARITY_COLORS.get(item['rarity'], '⬜')} {item['emoji']} **{item['name']}** [{item['slot']}]{eq}\n"
        embed.description = desc
        stock = get_consumables(ctx.author.id)
        if stock:
            parts = [f"{CONSUMABLES[c]['emoji']} {CONSUMABLES[c]['name']} x{q}" for c, q in stock.items() if c in CONSUMABLES]
            if parts:
                embed.add_field(name="🧴 Consumables", value="\n".join(parts), inline=False)
        embed.set_footer(text=f"Total: {len(items)} items | !equip <name> | !use <consumable>")
        await ctx.send(embed=embed)

    @commands.command()
    async def equip(self, ctx, *, item_name: str):
        """Equip an item by name."""
        item_id = next((iid for iid, d in ITEMS.items()
                        if d['name'].lower() == item_name.lower().strip()), None)
        if not item_id:
            await ctx.send("Item not found. Check `!inventory` for your items.", delete_after=10)
            return
        success, msg = equip_item(ctx.author.id, item_id)
        await ctx.send(f"{'✅' if success else '❌'} {msg}")

    @commands.group(invoke_without_command=True, aliases=['dungeon_top'])
    async def dungeon(self, ctx):
        """Dungeon leaderboard (top floors reached)."""
        await self._show_leaderboard(ctx)

    @dungeon.command(name="top")
    async def dungeon_top_cmd(self, ctx):
        """Dungeon leaderboard."""
        await self._show_leaderboard(ctx)

    @commands.command(name="dungeon_guide", aliases=["dguide", "advguide"])
    async def dungeon_guide(self, ctx):
        """Complete guide to the Dungeon Adventure system."""
        e1, e2 = build_guide_embeds()
        e3, e4 = build_guide_embeds_2()
        await ctx.send(embeds=[e1, e2, e3, e4])

    async def _show_leaderboard(self, ctx):
        db.cursor.execute(
            "SELECT user_id, deepest_floor, current_floor FROM adventure_character "
            "ORDER BY deepest_floor DESC LIMIT 10")
        rows = db.cursor.fetchall()
        desc = ""
        for i, r in enumerate(rows, 1):
            user = self.bot.get_user(int(r['user_id']))
            name = user.display_name if user else f"User {r['user_id']}"
            desc += f"**{i}.** {name} — Max Floor: {r['deepest_floor']} (Current: {r['current_floor']})\n"
        embed = discord.Embed(title="🏆 Dungeon Leaderboard", description=desc or "No data yet.", color=0xF1C40F)
        await ctx.send(embed=embed)


async def setup(bot):
    await bot.add_cog(Adventure(bot))
