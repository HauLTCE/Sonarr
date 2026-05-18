import discord
from discord.ext import commands
from utils.economy_helpers import get_balance, update_wallet, update_bank, ensure_account
from utils.database import db
from utils.achievements import unlock_achievement
from cogs.adventure.engine import get_character
from cogs.levels import get_level
from utils.consumables import CONSUMABLES, get_consumables, add_consumable, use_consumable, remove_consumable, SELL_PRICES
from cogs.adventure.items import (
    get_inventory, remove_item, ITEMS, RARITY_COLORS, RARITY_DISPLAY, GEAR_SELL_PRICES
)


# Title definitions with costs
COIN_TITLES = {
    "Coin Collector": 500,
    "High Roller": 2000,
    "Risk Taker": 5000,
    "Whale": 25000,
    "The One Percent": 100000,
}

GEM_TITLES = {
    "Lucky": 5,
    "Untouchable": 8,
    "Queen's Favorite": 10,
    "Dungeon Master": 15,
}


# ========== SHOP VIEW (Interactive Buttons) ==========

def _build_shop_main_embed(bal):
    embed = discord.Embed(title="🏪 Sonarr's Overpriced Shop", color=0x9B59B6)
    embed.description = (
        "\"Everything's fairly priced. You're just poor.\"\n\n"
        "Pick a category below.\n\n"
        f"👛 Wallet: {bal['wallet']:,} 🪙  |  💎 Gems: {bal['gems']}"
    )
    return embed


def _build_consumables_embed(user_id):
    bal = get_balance(user_id)
    stock = get_consumables(user_id)
    embed = discord.Embed(title="📦 Consumables", color=0x3498DB)
    lines = []
    for cid, cdata in CONSUMABLES.items():
        owned = stock.get(cid, 0)
        own_str = f"  ×{owned}" if owned > 0 else ""
        lines.append(f"{cdata['emoji']} **{cdata['name']}** — {cdata['cost']:,} 🪙{own_str}")
        lines.append(f"  *{cdata['desc']}*")
    embed.description = (
        "\n".join(lines) + f"\n\n"
        f"👛 Wallet: {bal['wallet']:,} 🪙\n"
        f"`!buy <name>` or `!buy <name> <qty>` to purchase."
    )
    return embed


def _build_upgrades_embed(user_id):
    bal = get_balance(user_id)
    embed = discord.Embed(title="⬆️ Upgrades", color=0x2ECC71)
    embed.description = (
        "1. 🏦 Bank Tier 1 (10,000 cap) — 2,000 🪙\n"
        "2. 🏦 Bank Tier 2 (25,000 cap) — 5,000 🪙\n"
        "3. 🏦 Bank Tier 3 (50,000 cap) — 15,000 🪙\n"
        "4. 🏦 Bank Tier 4 (100,000 cap) — 40,000 🪙\n"
        "5. 🏦 Bank Tier 5 (250,000 cap) — 100,000 🪙\n\n"
        f"👛 Wallet: {bal['wallet']:,} 🪙\n"
        f"Current bank cap: {bal['bank_cap']:,} 🪙\n"
        f"`!buy bank tier <N>` to upgrade."
    )
    return embed


def _build_titles_embed(user_id):
    bal = get_balance(user_id)
    embed = discord.Embed(title="🏷️ Titles", color=0xF1C40F)
    coin_list = "\n".join([f"• 「{n}」 — {c:,} 🪙" for n, c in COIN_TITLES.items()])
    gem_list = "\n".join([f"• 「{n}」 — {c} 💎" for n, c in GEM_TITLES.items()])
    embed.description = (
        f"**Coin Titles**\n{coin_list}\n\n"
        f"**Gem Titles**\n{gem_list}\n\n"
        f"👛 {bal['wallet']:,} 🪙  |  💎 {bal['gems']}\n"
        f"`!buy <title name>` to purchase."
    )
    return embed


def _build_sell_embed(user_id):
    embed = discord.Embed(title="💰 Sell Items", color=0xE67E22)
    items = get_inventory(user_id)
    unequipped = [i for i in items if not i['equipped']]
    stock = get_consumables(user_id)
    lines = []
    if unequipped:
        lines.append("**Gear** (unequipped only)")
        for item in unequipped[:15]:
            rarity_icon = RARITY_COLORS.get(item['rarity'], '⬜')
            price = GEAR_SELL_PRICES.get(item['rarity'], 10)
            lines.append(f"{rarity_icon} {item['emoji']} {item['name']} — {price} 🪙")
    if stock:
        lines.append("\n**Consumables**")
        for cid, qty in stock.items():
            if cid in CONSUMABLES:
                price = SELL_PRICES.get(cid, 5)
                lines.append(f"{CONSUMABLES[cid]['emoji']} {CONSUMABLES[cid]['name']} ×{qty} — {price} 🪙 each")
    if not lines:
        lines.append("Nothing to sell.")
    embed.description = "\n".join(lines) + "\n\n`!sell <item name>` to sell."
    return embed


class ShopView(discord.ui.View):
    def __init__(self, user_id):
        super().__init__(timeout=120)
        self.user_id = user_id

    async def interaction_check(self, interaction):
        return interaction.user.id == self.user_id

    @discord.ui.button(label="Consumables", style=discord.ButtonStyle.primary, emoji="📦")
    async def consumables(self, interaction, button):
        embed = _build_consumables_embed(self.user_id)
        view = ShopBackView(self.user_id)
        await interaction.response.edit_message(embed=embed, view=view)

    @discord.ui.button(label="Upgrades", style=discord.ButtonStyle.success, emoji="⬆️")
    async def upgrades(self, interaction, button):
        embed = _build_upgrades_embed(self.user_id)
        view = ShopBackView(self.user_id)
        await interaction.response.edit_message(embed=embed, view=view)

    @discord.ui.button(label="Titles", style=discord.ButtonStyle.secondary, emoji="🏷️")
    async def titles(self, interaction, button):
        embed = _build_titles_embed(self.user_id)
        view = ShopBackView(self.user_id)
        await interaction.response.edit_message(embed=embed, view=view)

    @discord.ui.button(label="Sell", style=discord.ButtonStyle.danger, emoji="💰")
    async def sell(self, interaction, button):
        embed = _build_sell_embed(self.user_id)
        view = ShopBackView(self.user_id)
        await interaction.response.edit_message(embed=embed, view=view)


class ShopBackView(discord.ui.View):
    def __init__(self, user_id):
        super().__init__(timeout=120)
        self.user_id = user_id

    async def interaction_check(self, interaction):
        return interaction.user.id == self.user_id

    @discord.ui.button(label="← Back to Shop", style=discord.ButtonStyle.secondary, emoji="🔙")
    async def back(self, interaction, button):
        bal = get_balance(self.user_id)
        embed = _build_shop_main_embed(bal)
        view = ShopView(self.user_id)
        await interaction.response.edit_message(embed=embed, view=view)


class Shop(commands.Cog):
    """Shop, Titles, and Progression"""
    def __init__(self, bot):
        self.bot = bot

    async def cog_check(self, ctx):
        """Allow shop commands in economy OR dungeon channel."""
        from cogs.games.utils import check_channel
        from utils.config import get_guild_config
        if ctx.guild:
            dungeon_ch = get_guild_config(ctx.guild.id, "dungeon_channel")
            if dungeon_ch and ctx.channel.id == int(dungeon_ch):
                return True
        return await check_channel(ctx, "economy_channel")

    @commands.group(invoke_without_command=True)
    async def shop(self, ctx):
        bal = get_balance(ctx.author.id)
        embed = _build_shop_main_embed(bal)
        view = ShopView(ctx.author.id)
        await ctx.send(embed=embed, view=view)

    @shop.command(name="consumables")
    async def shop_consumables(self, ctx):
        bal = get_balance(ctx.author.id)
        stock = get_consumables(ctx.author.id)
        embed = discord.Embed(title="📦 Consumables", color=0x3498DB)
        lines = []
        for cid, cdata in CONSUMABLES.items():
            owned = stock.get(cid, 0)
            own_str = f"  ×{owned}" if owned > 0 else ""
            lines.append(f"{cdata['emoji']} **{cdata['name']}** — {cdata['cost']:,} 🪙{own_str}")
            lines.append(f"  *{cdata['desc']}*")
        embed.description = (
            "\n".join(lines) + f"\n\n"
            f"Your wallet: {bal['wallet']:,} 🪙\n"
            f"Use `!buy <name>` to purchase. Use `!use <name>` in the dungeon."
        )
        await ctx.send(embed=embed)

    @shop.command(name="upgrades")
    async def shop_upgrades(self, ctx):
        bal = get_balance(ctx.author.id)
        embed = discord.Embed(title="⬆️ Upgrades", color=0x2ECC71)
        embed.description = (
            "1. 🏦 Bank Tier 1 (10,000 cap) — 2,000 🪙\n"
            "2. 🏦 Bank Tier 2 (25,000 cap) — 5,000 🪙\n"
            "3. 🏦 Bank Tier 3 (50,000 cap) — 15,000 🪙\n"
            "4. 🏦 Bank Tier 4 (100,000 cap) — 40,000 🪙\n"
            "5. 🏦 Bank Tier 5 (250,000 cap) — 100,000 🪙\n\n"
            f"Your wallet: {bal['wallet']:,} 🪙\n"
            f"Current bank cap: {bal['bank_cap']:,} 🪙"
        )
        await ctx.send(embed=embed)

    @shop.command(name="titles")
    async def shop_titles(self, ctx):
        bal = get_balance(ctx.author.id)
        embed = discord.Embed(title="🏷️ Titles", color=0xF1C40F)

        coin_list = "\n".join([f"• 「{name}」 — {cost:,} 🪙" for name, cost in COIN_TITLES.items()])
        gem_list = "\n".join([f"• 「{name}」 — {cost} 💎" for name, cost in GEM_TITLES.items()])

        embed.description = (
            f"**Coin Titles**\n{coin_list}\n\n"
            f"**Gem Titles**\n{gem_list}\n\n"
            f"Your wallet: {bal['wallet']:,} 🪙  |  Gems: {bal['gems']} 💎\n\n"
            f"Use `!buy <title name>` to purchase.\n"
            f"Use `!title <name>` to switch your active title."
        )
        await ctx.send(embed=embed)

    @commands.command()
    async def buy(self, ctx, *, item_name: str):
        """Purchase an item, upgrade, or title"""
        user_id = str(ctx.author.id)
        bal = get_balance(ctx.author.id)
        name_lower = item_name.lower().strip()

        # Bank upgrades
        if name_lower.startswith("bank tier"):
            try:
                tier = int(name_lower.split()[-1])
            except Exception:
                await ctx.send("Invalid bank tier.")
                return
            caps = {1: 10000, 2: 25000, 3: 50000, 4: 100000, 5: 250000}
            costs = {1: 2000, 2: 5000, 3: 15000, 4: 40000, 5: 100000}
            if tier not in caps:
                await ctx.send("Invalid tier.")
                return
            if bal['bank_cap'] >= caps[tier]:
                await ctx.send("You already have this or a higher bank tier.")
                return
            if bal['wallet'] < costs[tier]:
                await ctx.send(f"You need {costs[tier]:,} 🪙 to buy this upgrade.")
                return
            update_wallet(ctx.author.id, -costs[tier])
            db.cursor.execute("UPDATE economy SET bank_cap = ? WHERE user_id = ?", (caps[tier], user_id))
            db.connection.commit()
            await ctx.send(f"✅ Bank upgraded to Tier {tier}! New capacity: {caps[tier]:,} 🪙.")
            return

        # Coin titles
        for title_name, cost in COIN_TITLES.items():
            if title_name.lower() == name_lower:
                # Check if already owned
                ach_id = f"title_{title_name}"
                db.cursor.execute("SELECT 1 FROM achievements WHERE user_id = ? AND achievement_id = ?", (user_id, ach_id))
                if db.cursor.fetchone():
                    await ctx.send(f"You already own 「{title_name}」. Use `!title {title_name}` to equip it.")
                    return
                if bal['wallet'] < cost:
                    await ctx.send(f"You need {cost:,} 🪙 for 「{title_name}」.")
                    return
                update_wallet(ctx.author.id, -cost)
                unlock_achievement(ctx.author.id, ach_id)
                # Auto-set as active title
                db.cursor.execute("UPDATE economy SET active_title = ? WHERE user_id = ?", (title_name, user_id))
                db.connection.commit()
                # Assign Discord role
                await self._assign_title_role(ctx.author, title_name, "economy")
                await ctx.send(f"✅ Purchased and equipped 「{title_name}」!")
                return

        # Gem titles
        for title_name, cost in GEM_TITLES.items():
            if title_name.lower() == name_lower:
                ach_id = f"title_{title_name}"
                db.cursor.execute("SELECT 1 FROM achievements WHERE user_id = ? AND achievement_id = ?", (user_id, ach_id))
                if db.cursor.fetchone():
                    await ctx.send(f"You already own 「{title_name}」. Use `!title {title_name}` to equip it.")
                    return
                if bal['gems'] < cost:
                    await ctx.send(f"You need {cost} 💎 for 「{title_name}」.")
                    return
                db.cursor.execute("UPDATE economy SET gems = gems - ? WHERE user_id = ?", (cost, user_id))
                db.connection.commit()
                unlock_achievement(ctx.author.id, ach_id)
                db.cursor.execute("UPDATE economy SET active_title = ? WHERE user_id = ?", (title_name, user_id))
                db.connection.commit()
                await self._assign_title_role(ctx.author, title_name, "gem")
                await ctx.send(f"✅ Purchased and equipped 「{title_name}」!")
                return

        # Consumables — supports bulk buy: "!buy health potion 5"
        buy_qty = 1
        consumable_match = None
        for cid, cdata in CONSUMABLES.items():
            if cdata['name'].lower() == name_lower:
                consumable_match = (cid, cdata)
                break
            # Check for trailing quantity: "health potion 5"
            parts = name_lower.rsplit(maxsplit=1)
            if len(parts) == 2 and parts[1].isdigit() and cdata['name'].lower() == parts[0]:
                buy_qty = max(1, min(int(parts[1]), 99))
                consumable_match = (cid, cdata)
                break

        if consumable_match:
            cid, cdata = consumable_match
            total_cost = cdata['cost'] * buy_qty
            if bal['wallet'] < total_cost:
                if buy_qty > 1:
                    await ctx.send(f"You need {total_cost:,} 🪙 for {buy_qty}× **{cdata['name']}** ({cdata['cost']:,} each).")
                else:
                    await ctx.send(f"You need {cdata['cost']:,} 🪙 for **{cdata['name']}**.")
                return
            update_wallet(ctx.author.id, -total_cost)
            add_consumable(ctx.author.id, cid, buy_qty)
            stock = get_consumables(ctx.author.id)
            qty_str = f"{buy_qty}× " if buy_qty > 1 else ""
            await ctx.send(f"✅ Bought {qty_str}**{cdata['name']}**! You now have {stock.get(cid, 1)}. Use `!use {cdata['name'].lower()}` in the dungeon.")
            return

        await ctx.send("Item not found. Check `!shop` for available items.")

    async def _assign_title_role(self, member: discord.Member, title_name: str, category: str):
        """Assign a title as a Discord role."""
        try:
            from utils.roles import assign_title_role
            await assign_title_role(member, title_name, category)
        except Exception as e:
            import logging
            logging.getLogger("bot").warning(f"[Shop] Failed to assign role: {e}")

    @commands.command(aliases=['titles'])
    async def title(self, ctx, *, title_name: str = None):
        """View owned titles or switch active title"""
        ensure_account(ctx.author.id)
        user_id = str(ctx.author.id)

        if not title_name:
            # Show owned titles
            db.cursor.execute("SELECT achievement_id FROM achievements WHERE user_id = ? AND achievement_id LIKE 'title_%'", (user_id,))
            owned = [row[0].replace("title_", "") for row in db.cursor.fetchall()]

            db.cursor.execute("SELECT active_title FROM economy WHERE user_id = ?", (user_id,))
            row = db.cursor.fetchone()
            active = row[0] if row and row[0] else "None"

            embed = discord.Embed(title="🏷️ Your Titles", color=0xF1C40F)
            if owned:
                desc = "\n".join([f"{'✅' if t == active else '⬜'} 「{t}」" for t in owned])
            else:
                desc = "No titles owned. Visit `!shop titles` to buy some."
            embed.description = f"**Active:** 「{active}」\n\n{desc}\n\nUse `!title <name>` to switch, or `!title none` to remove."
            await ctx.send(embed=embed)
            return

        # Remove title
        if title_name.lower() == "none":
            db.cursor.execute("UPDATE economy SET active_title = NULL WHERE user_id = ?", (user_id,))
            db.connection.commit()
            try:
                from utils.roles import remove_title_roles
                await remove_title_roles(ctx.author)
            except Exception:
                pass
            await ctx.send("Title removed.")
            return

        # Check ownership
        ach_id = f"title_{title_name}"
        db.cursor.execute("SELECT 1 FROM achievements WHERE user_id = ? AND achievement_id = ?", (user_id, ach_id))
        if not db.cursor.fetchone():
            await ctx.send(f"You don't own 「{title_name}」. Buy it first from `!shop titles`.")
            return

        db.cursor.execute("UPDATE economy SET active_title = ? WHERE user_id = ?", (title_name, user_id))
        db.connection.commit()

        # Determine category for role color
        category = "economy"
        if title_name in GEM_TITLES:
            category = "gem"
        elif title_name.startswith("Floor"):
            category = "dungeon"
        elif title_name.startswith("Prestige"):
            category = "prestige"

        await self._assign_title_role(ctx.author, title_name, category)
        await ctx.send(f"✅ Title set to: 「{title_name}」")

    @commands.command(aliases=['ach'])
    async def achievements(self, ctx):
        from utils.achievements import get_achievements
        achs = get_achievements(ctx.author.id)
        
        embed = discord.Embed(title=f"🏅 {ctx.author.display_name}'s Achievements", color=0xF1C40F)
        if achs:
            embed.description = "\n".join([f"• {a}" for a in achs])
        else:
            embed.description = "No achievements yet."
            
        embed.set_footer(text=f"Total: {len(achs)}")
        await ctx.send(embed=embed)

    @commands.command()
    async def profile(self, ctx, member: discord.Member = None):
        """View user profile"""
        member = member or ctx.author
        user_id = str(member.id)
        
        bal = get_balance(member.id)
        char = get_character(member.id)
        
        db.cursor.execute("SELECT active_title, prestige, total_earned, total_gambled, daily_streak FROM economy WHERE user_id = ?", (user_id,))
        econ_data = db.cursor.fetchone()
        
        if not econ_data:
            await ctx.send("No economy data found for this user.")
            return
        
        db.cursor.execute("SELECT SUM(games_played), SUM(games_won), MAX(biggest_win), MAX(biggest_loss) FROM gambling_stats WHERE user_id = ?", (user_id,))
        g_data = db.cursor.fetchone()
        
        g_played = (g_data[0] or 0) if g_data else 0
        g_won = (g_data[1] or 0) if g_data else 0
        b_win = (g_data[2] or 0) if g_data else 0
        b_loss = (g_data[3] or 0) if g_data else 0
        win_rate = (g_won / g_played * 100) if g_played > 0 else 0
        
        from utils.achievements import get_achievements
        achs = get_achievements(member.id)
        
        embed = discord.Embed(title=f"👤 {member.display_name}", color=0x3498DB)
        
        active_title = econ_data['active_title'] if econ_data['active_title'] else "None"
        prestige_val = econ_data['prestige'] or 0
        prestige_str = "⭐" * prestige_val if prestige_val > 0 else "0"
        
        # Use DB-backed levels
        guild_id = str(ctx.guild.id) if ctx.guild else "global"
        level_data = get_level(member.id, guild_id)
        level = level_data["level"]
        
        total_earned = econ_data['total_earned'] or 0
        total_gambled = econ_data['total_gambled'] or 0
        daily_streak = econ_data['daily_streak'] or 0
        
        embed.description = (
            f"Title: 「{active_title}」\n"
            f"Level: {level}  |  Prestige: {prestige_str}\n\n"
            f"**💰 Economy**\n"
            f"Wallet: {bal['wallet']:,} 🪙  |  Bank: {bal['bank']:,} 🪙\n"
            f"Gems: {bal['gems']:,} 💎  |  Net Worth: {bal['wallet'] + bal['bank']:,} 🪙\n"
            f"Lifetime Earned: {total_earned:,} 🪙\n"
            f"Lifetime Gambled: {total_gambled:,} 🪙\n\n"
            f"**🎰 Gambling Stats**\n"
            f"Games Played: {g_played}  |  Win Rate: {win_rate:.1f}%\n"
            f"Biggest Win: {b_win:,} 🪙\n"
            f"Biggest Loss: {b_loss:,} 🪙\n\n"
            f"**🗡️ Adventure**\n"
            f"Deepest Floor: {char['deepest_floor']}  |  Bosses Killed: {char['bosses_killed']}\n\n"
            f"**🏅 Achievements:** {len(achs)} unlocked\n"
            f"**📅 Daily Streak:** {daily_streak} days"
        )
        
        await ctx.send(embed=embed)

    @commands.command()
    async def prestige(self, ctx):
        """Reset progress for permanent bonuses"""
        user_id = str(ctx.author.id)
        
        guild_id = str(ctx.guild.id) if ctx.guild else "global"
        level_data = get_level(ctx.author.id, guild_id)
        level = level_data["level"]
        
        db.cursor.execute("SELECT total_earned, prestige FROM economy WHERE user_id = ?", (user_id,))
        econ_data = db.cursor.fetchone()
        
        char = get_character(ctx.author.id)
        
        if level < 100 or econ_data['total_earned'] < 10000 or char['deepest_floor'] < 25:
            await ctx.send("You do not meet the requirements to prestige. Requires:\n- Level 100+\n- 10,000+ Lifetime Earned\n- Dungeon Floor 25+")
            return
            
        if econ_data['prestige'] >= 5:
            await ctx.send("You are already at max prestige (5).")
            return
            
        new_prestige = econ_data['prestige'] + 1

        class PrestigeConfirm(discord.ui.View):
            def __init__(self, outer_ctx, prestige_level):
                super().__init__(timeout=60)
                self.outer_ctx = outer_ctx
                self.prestige_level = prestige_level
                
            @discord.ui.button(label="Prestige Now", style=discord.ButtonStyle.danger, emoji="⭐")
            async def confirm(self, interaction: discord.Interaction, button: discord.ui.Button):
                if interaction.user.id != self.outer_ctx.author.id:
                    return
                db.cursor.execute("UPDATE economy SET wallet = 0, bank = 0, prestige = prestige + 1 WHERE user_id = ?", (str(self.outer_ctx.author.id),))
                db.cursor.execute("UPDATE adventure_character SET current_floor = 1, deepest_floor = 1 WHERE user_id = ?", (str(self.outer_ctx.author.id),))
                db.connection.commit()
                
                set_level(self.outer_ctx.author.id, guild_id, level=1, xp=0)
                
                # Auto-grant prestige title
                prestige_title = f"Prestige {['I','II','III','IV','V'][self.prestige_level - 1]}"
                unlock_achievement(self.outer_ctx.author.id, f"title_{prestige_title}")
                db.cursor.execute("UPDATE economy SET active_title = ? WHERE user_id = ?", (prestige_title, str(self.outer_ctx.author.id)))
                db.connection.commit()

                # Assign role
                try:
                    from utils.roles import assign_title_role
                    await assign_title_role(interaction.user, prestige_title, "prestige")
                except Exception:
                    pass
                    
                await interaction.response.edit_message(content=f"⭐ **Prestige {prestige_title} successful!** You are reborn.", embed=None, view=None)
                self.stop()
                
            @discord.ui.button(label="Nevermind", style=discord.ButtonStyle.secondary, emoji="❌")
            async def cancel(self, interaction: discord.Interaction, button: discord.ui.Button):
                if interaction.user.id != self.outer_ctx.author.id:
                    return
                await interaction.response.edit_message(content="Prestige cancelled.", embed=None, view=None)
                self.stop()

        embed = discord.Embed(title="⭐ PRESTIGE", color=0xF1C40F)
        embed.description = (
            "Are you SURE? This will reset:\n"
            f"❌ Level {level} → Level 1\n"
            f"❌ Wallet & Bank → 0 🪙\n"
            f"❌ Dungeon Floor {char['current_floor']} → Floor 1\n\n"
            "You KEEP:\n"
            "✅ All gear, items, achievements, titles, and gems\n\n"
            "You GAIN:\n"
            f"🌟 Prestige ⭐ (+ title role)\n"
        )
        
        view = PrestigeConfirm(ctx, new_prestige)
        await ctx.send(embed=embed, view=view)

    @commands.command()
    async def sell(self, ctx, *, item_name: str):
        """Sell an item from your inventory"""
        name_lower = item_name.lower().strip()

        # Try consumables first
        for cid, cdata in CONSUMABLES.items():
            if cdata['name'].lower() == name_lower:
                if not remove_consumable(ctx.author.id, cid, 1):
                    await ctx.send(f"You don't have any **{cdata['name']}** to sell.")
                    return
                price = SELL_PRICES.get(cid, 5)
                update_wallet(ctx.author.id, price)
                await ctx.send(f"💰 Sold **{cdata['name']}** for {price} 🪙.")
                return

        # Try gear
        for iid, idata in ITEMS.items():
            if idata['name'].lower() == name_lower:
                items = get_inventory(ctx.author.id)
                match = None
                for inv in items:
                    if inv['item_id'] == iid and not inv['equipped']:
                        match = inv
                        break
                if not match:
                    await ctx.send(f"You don't have an unequipped **{idata['name']}** to sell.")
                    return
                if not remove_item(ctx.author.id, match['inv_id']):
                    await ctx.send("Failed to sell. Is it equipped?")
                    return
                price = GEAR_SELL_PRICES.get(idata['rarity'], 10)
                update_wallet(ctx.author.id, price)
                rarity_icon = RARITY_COLORS.get(idata['rarity'], '⬜')
                await ctx.send(f"💰 Sold {rarity_icon} **{idata['name']}** for {price} 🪙.")
                return

        await ctx.send("Item not found. Check `!inventory` for your items.")


async def setup(bot):
    await bot.add_cog(Shop(bot))
