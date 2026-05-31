import discord
from discord.ext import commands
import time
import random

from utils.database import db
from utils.economy_helpers import (
    ensure_account, get_balance, update_wallet, update_bank,
    get_leaderboard, calculate_cashout, spend_wallet
)
from cogs.levels import get_level, set_level

# ===== SUGGESTION VIEWS =====

class BankUpgradeView(discord.ui.View):
    """Suggestion button to upgrade bank when full."""
    def __init__(self, ctx):
        super().__init__(timeout=30)
        self.ctx = ctx

    @discord.ui.button(label="Upgrade Bank", style=discord.ButtonStyle.success, emoji="⬆️")
    async def upgrade(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            return
        bal = get_balance(self.ctx.author.id)
        caps = {1: 10000, 2: 25000, 3: 50000, 4: 100000, 5: 250000}
        costs = {1: 2000, 2: 5000, 3: 15000, 4: 40000, 5: 100000}
        next_tier = None
        for tier, cap in caps.items():
            if bal['bank_cap'] < cap:
                next_tier = tier
                break
        if not next_tier:
            await interaction.response.send_message("Already at max bank tier!", ephemeral=True)
            return
        cost = costs[next_tier]
        if not spend_wallet(self.ctx.author.id, cost):
            await interaction.response.send_message(f"Need {cost:,} 🪙 for Bank Tier {next_tier}.", ephemeral=True)
            return
        uid = str(self.ctx.author.id)
        db.cursor.execute("UPDATE economy SET bank_cap = ? WHERE user_id = ?", (caps[next_tier], uid))
        db.connection.commit()
        await interaction.response.edit_message(
            content=f"✅ Bank upgraded to Tier {next_tier}! New cap: {caps[next_tier]:,} 🪙.", view=None)
        self.stop()


class CashoutView(discord.ui.View):
    def __init__(self, ctx, levels_to_sell, payout, new_level, cog):
        super().__init__(timeout=60)
        self.ctx = ctx
        self.levels_to_sell = levels_to_sell
        self.payout = payout
        self.new_level = new_level
        self.cog = cog
        self.value = None
        self.done = False  # Guards against double-confirm (two views → double payout)

    @discord.ui.button(label="Confirm", style=discord.ButtonStyle.green, emoji="✅")
    async def confirm(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your button.", ephemeral=True)
            return
        self.value = True
        await self.process_cashout(interaction)
        self.stop()

    @discord.ui.button(label="Cancel", style=discord.ButtonStyle.red, emoji="❌")
    async def cancel(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your button.", ephemeral=True)
            return
        self.value = False
        await interaction.response.edit_message(content="Cashout cancelled.", embed=None, view=None)
        self.stop()
        
    async def process_cashout(self, interaction):
        # Re-entry guard: if the user opened two cashout views, only the first
        # confirm pays out — the rest are no-ops.
        if self.done:
            await interaction.response.send_message("This cashout was already processed.", ephemeral=True)
            return
        self.done = True
        for child in self.children:
            child.disabled = True

        user_id = str(self.ctx.author.id)
        guild_id = str(interaction.guild.id) if interaction.guild else 'global'
        
        set_level(self.ctx.author.id, guild_id, level=self.new_level, xp=0)
        
        update_wallet(self.ctx.author.id, self.payout)
        
        now = time.time()
        db.cursor.execute("UPDATE economy SET last_cashout = ? WHERE user_id = ?", (now, user_id))
        db.connection.commit()
        
        msg = random.choice([
            f"{self.payout} coins for {self.levels_to_sell} levels of your life. Was it worth it. Don't answer that.",
            "Sold. Your levels are gone forever. No refunds, no regrets, no therapy.",
            "You traded your progress for pocket change. Bold financial strategy."
        ])
        
        await interaction.response.edit_message(content=msg, embed=None, view=None)

WORK_SUCCESS = [
    ("🧹 You cleaned Sonarr's code and found {amount} coins in the bugs.", None),
    ("📦 You delivered packages across the server. Earned {amount} coins.", None),
    ("🎨 You drew fanart of Sonarr (without permission). She tolerated it. +{amount} coins.", None),
    ("🔧 You fixed a typo in the bot's responses. Sonarr didn't notice. +{amount} coins.", None),
    ("📊 You organized the server channels. Nobody thanked you. +{amount} coins.", None),
    ("🧪 You beta-tested Sonarr's new insults. +{amount} coins for emotional damage.", None),
    ("🎵 You tuned the Lavalink server. The music quality improved by 0.01%. +{amount} coins.", None),
    ("📝 You wrote documentation nobody will read. +{amount} coins.", None),
    ("🗑️ You took out the server trash. Found {amount} coins in the garbage.", None),
    ("☕ You made Sonarr's morning coffee. She didn't say thank you. +{amount} coins.", None),
]

WORK_CRIT_SUCCESS = [
    "You found a hidden treasure chest while cleaning! DOUBLE PAY: {amount} coins.",
    "Sonarr accidentally left her wallet out. You 'found' {amount} coins.",
    "A mysterious benefactor tipped you {amount} coins. Suspicious.",
]

WORK_FAIL = [
    "You tried to work but fell asleep at your desk. No pay.",
    "You accidentally deleted production. Sonarr is furious. No coins for you.",
    "You showed up but the office was closed. Tough luck.",
    "You worked hard but your invoice was lost. Zero coins.",
    "You tried but Sonarr said your work was 'mid'. No payment.",
]

class Economy(commands.Cog):
    """Core Economy Commands"""

    def __init__(self, bot):
        self.bot = bot

    async def cog_check(self, ctx):
        """Enforce economy channel for all economy commands."""
        from cogs.games.utils import check_channel
        return await check_channel(ctx, "economy_channel")

    @commands.command()
    async def cashout(self, ctx, levels: int):
        """Sell levels for coins"""
        if levels <= 0:
            await ctx.send("You need to sell at least 1 level.")
            return

        user_id = str(ctx.author.id)
        ensure_account(ctx.author.id)
        
        # Check cooldown (1 hour)
        db.cursor.execute("SELECT last_cashout FROM economy WHERE user_id = ?", (user_id,))
        row = db.cursor.fetchone()
        if row and row[0]:
            last_cashout = row[0]
            if time.time() - last_cashout < 3600:
                rem = int(3600 - (time.time() - last_cashout))
                wait = f"{rem // 60}m {rem % 60}s" if rem >= 60 else f"{rem}s"
                await ctx.send(f"You can cash out again in {wait}.")
                return

        guild_id = str(ctx.guild.id) if ctx.guild else 'global'
        level_data = get_level(ctx.author.id, guild_id)
        current_level = level_data["level"]
        
        if current_level <= 1:
            await ctx.send("You have no levels to sell.")
            return
        
        if levels >= current_level:
            levels = current_level - 1
            
        if levels <= 0:
            await ctx.send("You don't have enough levels to cash out.")
            return
            
        payout = calculate_cashout(levels)
        new_level = current_level - levels
        
        if levels >= 10:
            embed = discord.Embed(title="🪙 Level Cashout", color=0xFFD700)
            embed.description = (
                f"Selling: {levels} levels\n"
                f"Current Level: {current_level} → {new_level}\n"
                f"Payout: {payout} coins\n\n"
                f"⚠️ Your XP will reset to 0 for Level {new_level}. This cannot be undone."
            )
            view = CashoutView(ctx, levels, payout, new_level, self)
            await ctx.send(embed=embed, view=view)
        else:
            set_level(ctx.author.id, guild_id, level=new_level, xp=0)
            
            update_wallet(ctx.author.id, payout)
            
            now = time.time()
            db.cursor.execute("UPDATE economy SET last_cashout = ? WHERE user_id = ?", (now, user_id))
            db.connection.commit()
            
            msg = random.choice([
                f"{payout} coins for {levels} levels of your life. Was it worth it. Don't answer that.",
                "Sold. Your levels are gone forever. No refunds, no regrets, no therapy.",
                "You traded your progress for pocket change. Bold financial strategy."
            ])
            await ctx.send(msg)

    @commands.command(aliases=['bal'])
    async def balance(self, ctx):
        """Check your wallet and bank balance"""
        bal = get_balance(ctx.author.id)
        
        embed = discord.Embed(title=f"💳 Balance — {ctx.author.display_name}", color=0x2ECC71)
        embed.description = (
            f"👛 Wallet:  {bal['wallet']:,} 🪙\n"
            f"🏦 Bank:    {bal['bank']:,} / {bal['bank_cap']:,} 🪙\n"
            f"💎 Gems:    {bal['gems']:,}\n\n"
            f"📊 Net Worth: {bal['wallet'] + bal['bank']:,} 🪙"
        )
        
        net_worth = bal['wallet'] + bal['bank']
        
        if net_worth == 0:
            content = "0 coins. Congratulations, you've achieved nothing."
        elif bal['wallet'] < 100:
            content = f"{bal['wallet']:,} coins in wallet. That's not a balance, that's a cry for help."
        else:
            content = f"Your net worth is {net_worth:,} coins. In this economy, that's almost respectable."

        view = None
        if bal['bank'] >= bal['bank_cap'] and bal['bank_cap'] < 250000:
            view = BankUpgradeView(ctx)
        await ctx.send(content=content, embed=embed, view=view)

    @commands.command()
    async def daily(self, ctx):
        """Claim your daily reward"""
        user_id = str(ctx.author.id)
        ensure_account(ctx.author.id)
        
        db.cursor.execute("SELECT last_daily, daily_streak FROM economy WHERE user_id = ?", (user_id,))
        row = db.cursor.fetchone()
        last_daily = row[0]
        streak = row[1]
        
        now = time.time()
        hours_since = (now - last_daily) / 3600
        
        if hours_since < 24:
            rem_secs = int(24 * 3600 - (now - last_daily))
            if rem_secs >= 3600:
                wait = f"{rem_secs // 3600}h {(rem_secs % 3600) // 60}m"
            else:
                wait = f"{rem_secs // 60}m"
            await ctx.send(f"I already gave you your allowance. Come back in {wait}.")
            return
            
        if hours_since > 48 and last_daily != 0:
            streak = 0
        else:
            if last_daily != 0:
                streak += 1
                
        base_reward = 50
        bonus = min(streak * 10, 100)
        payout = base_reward + bonus
        
        gems_reward = 0
        if streak == 7:
            gems_reward = 1
        elif streak == 30:
            gems_reward = 3
            
        update_wallet(ctx.author.id, payout)
        if gems_reward > 0:
            db.cursor.execute("UPDATE economy SET gems = gems + ? WHERE user_id = ?", (gems_reward, user_id))
            
        db.cursor.execute("UPDATE economy SET last_daily = ?, daily_streak = ? WHERE user_id = ?", (now, streak, user_id))
        db.connection.commit()
        
        embed = discord.Embed(title="📅 Daily Reward", color=0xF1C40F)
        desc = (
            f"Base:     {base_reward} 🪙\n"
            f"Streak:   +{bonus} 🪙 (Day {streak})\n"
            f"Total:    {payout} 🪙\n\n"
        )
        
        if gems_reward > 0:
            desc += f"🎉 Milestone reached! You got {gems_reward} 💎!\n"
        elif streak < 7:
            remaining = 7 - streak
            day_word = "day" if remaining == 1 else "days"
            desc += f"🔥 {streak}-day streak! {remaining} more {day_word} for a 💎\nCome back tomorrow to keep it."
            
        embed.description = desc
        
        msg = random.choice([
            "Your daily crumbs. You're welcome.",
            "Here. Take it. And don't come back for 24 hours.",
            f"Day {streak} streak. I'm genuinely confused by your commitment." if streak >= 7 else "Here's your daily money."
        ])
        
        await ctx.send(content=msg, embed=embed)

    @commands.command()
    async def work(self, ctx):
        """Earn coins via mini-task"""
        user_id = str(ctx.author.id)
        ensure_account(ctx.author.id)
        
        db.cursor.execute("SELECT last_work FROM economy WHERE user_id = ?", (user_id,))
        row = db.cursor.fetchone()
        last_work = row[0]
        
        now = time.time()
        if now - last_work < 7200:
            rem = int((7200 - (now - last_work)) // 60)
            await ctx.send(f"You already worked. Take a break for {rem} minutes.")
            return
            
        db.cursor.execute("UPDATE economy SET last_work = ? WHERE user_id = ?", (now, user_id))
        db.connection.commit()
        
        roll = random.random()
        if roll < 0.05:
            # Crit fail
            msg = random.choice(WORK_FAIL)
            await ctx.send(msg)
            return
        elif roll < 0.10:
            # Crit success
            amount = random.randint(60, 160)
            update_wallet(ctx.author.id, amount)
            msg = random.choice(WORK_CRIT_SUCCESS).format(amount=amount)
            await ctx.send(msg)
            return
        else:
            amount = random.randint(30, 80)
            update_wallet(ctx.author.id, amount)
            msg_tuple = random.choice(WORK_SUCCESS)
            msg = msg_tuple[0].format(amount=amount)
            await ctx.send(msg)

    @commands.command()
    async def pay(self, ctx, user: discord.Member, amount: int):
        """Send coins to someone"""
        if amount < 10:
            await ctx.send("Minimum transfer is 10 coins.")
            return
            
        if user.id == ctx.author.id:
            await ctx.send("You can't pay yourself. Weirdo.")
            return
            
        if user.bot:
            await ctx.send("Bots don't need your money.")
            return
            
        bal = get_balance(ctx.author.id)
        if bal["wallet"] < amount:
            await ctx.send("You don't have enough coins in your wallet.")
            return
            
        tax = int(amount * 0.05)
        net_amount = amount - tax

        # Atomic: deduct the full amount from the sender first; only credit the
        # recipient if that succeeded, so a race can't duplicate or lose coins.
        if not spend_wallet(ctx.author.id, amount):
            await ctx.send("You don't have enough coins in your wallet.")
            return
        update_wallet(user.id, net_amount)

        await ctx.send(f"You paid {user.mention} {net_amount} 🪙. ({tax} 🪙 taken as tax.)")

    @commands.command(aliases=['dep'])
    async def deposit(self, ctx, amount: str):
        """Move coins from wallet to bank"""
        bal = get_balance(ctx.author.id)
        
        if amount.lower() == "all":
            dep_amount = bal["wallet"]
        else:
            try:
                dep_amount = int(amount)
            except ValueError:
                await ctx.send("Invalid amount.")
                return
                
        if dep_amount <= 0:
            await ctx.send("Deposit must be positive.")
            return
            
        if bal["wallet"] < dep_amount:
            await ctx.send("You don't have that much in your wallet.")
            return
            
        if bal["bank"] + dep_amount > bal["bank_cap"]:
            dep_amount = bal["bank_cap"] - bal["bank"]
            
        if dep_amount <= 0:
            view = BankUpgradeView(ctx)
            await ctx.send("Your bank is full. Upgrade?", view=view)
            return

        # Atomic: pull from wallet first; only bank it if the deduction happened.
        if not spend_wallet(ctx.author.id, dep_amount):
            await ctx.send("You don't have that much in your wallet.")
            return
        update_bank(ctx.author.id, dep_amount)

        await ctx.send(f"Deposited {dep_amount} 🪙 to your bank.")

    @commands.command()
    async def withdraw(self, ctx, amount: str):
        """Move coins from bank to wallet"""
        bal = get_balance(ctx.author.id)
        
        if amount.lower() == "all":
            wd_amount = bal["bank"]
        else:
            try:
                wd_amount = int(amount)
            except ValueError:
                await ctx.send("Invalid amount.")
                return
                
        if wd_amount <= 0:
            await ctx.send("Withdrawal must be positive.")
            return
            
        if bal["bank"] < wd_amount:
            await ctx.send("You don't have that much in your bank.")
            return

        # Atomic: deduct from bank only if it still has the funds, then credit wallet.
        uid = str(ctx.author.id)
        db.cursor.execute(
            "UPDATE economy SET bank = bank - ? WHERE user_id = ? AND bank >= ?",
            (wd_amount, uid, wd_amount),
        )
        db.connection.commit()
        if db.cursor.rowcount == 0:
            await ctx.send("You don't have that much in your bank.")
            return
        update_wallet(ctx.author.id, wd_amount)

        await ctx.send(f"Withdrew {wd_amount} 🪙 from your bank.")

    @commands.command()
    async def baltop(self, ctx):
        """Server-wide wealth leaderboard"""
        top_users = get_leaderboard(10)
        
        desc = ""
        for i, u in enumerate(top_users, 1):
            user = self.bot.get_user(int(u['user_id']))
            name = user.display_name if user else f"User {u['user_id']}"
            desc += f"{i}. {name} — {u['total']:,} 🪙\n"
            
        embed = discord.Embed(title="🏆 Wealthiest Users", description=desc, color=0xF1C40F)
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Economy(bot))
