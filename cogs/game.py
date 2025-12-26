import discord
from discord.ext import commands
import json
import os
import random
import logging
from datetime import datetime, timezone, timedelta, time as datetime_time

from .views import TicTacToeView
from utils.economy import EconomyManager
from utils.internal_commands import InternalCommandResult, InternalCommandExecutor

logger = logging.getLogger("bot")

SHOP_FILE = "shop.json"
ITEMS_FILE = "items.json"

class Games(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
        self.items = self.load_items()
        self.donation_responses = [
            "Oh, how *generous* of you. I suppose I'll accept this pittance.",
            "Finally, someone with taste. This goes straight to my collection.",
            "Darling, you're making excellent life choices. Keep it up.",
            "Mmm, yes. This will look lovely in my vault.",
            "You're learning. Perhaps you're not completely hopeless after all.",
            "A token of your devotion? How *adorable*. I'll treasure it.",
            "Well, well, well. Someone's trying to impress me. It's working.",
            "This better be worth my time... Actually, it is. Well done.",
            "I accept your tribute with *grace* and *dignity*.",
            "Spending money on me? You have exquisite judgment, truly.",
            "How delightfully thoughtful. You're growing on me.",
            "Yes, yes, shower me with your wealth. I'm listening.",
            "Ugh, finally. I was beginning to think you had no sense of appreciation.",
            "You're really spoiling me, aren't you? Not that I mind.",
            "This will look *perfect* next to all my other treasures.",
            "Oh darling, you shouldn't have. But I'm glad you did.",
            "Your dedication to my enrichment is *noted*. Favorably.",
            "I'm touched. Literally. By the money. It's in my wallet now.",
            "You know what? You might actually be worthy of my presence.",
            "A new contender for my affections? This is promising..."
        ]

    def load_items(self):
        if not os.path.exists(ITEMS_FILE):
            logger.warning("[Items] items.json not found")
            return {}
        try:
            with open(ITEMS_FILE, "r", encoding="utf-8") as f:
                data = json.load(f)
                if isinstance(data, dict):
                    return data
        except Exception as e:
            logger.error(f"[Items] Failed to load items: {e}")
        return {}

    def find_item(self, query):
        key = query.lower().strip()
        if key in self.items:
            return key, self.items[key]
        for item_id, item in self.items.items():
            name = str(item.get("name", "")).lower()
            if name == key:
                return item_id, item
        return None, None

    def get_balance(self, user_id, location="wallet"):
        return self.economy_manager.get_balance(user_id, location)

    def update_balance(self, user_id, amount, location="wallet"):
        self.economy_manager.update_balance(user_id, amount, location)
    
    def internal_get_balance(self, user_id):
        """Internal: Get balance without user interaction"""
        wallet = self.get_balance(user_id, "wallet")
        bank = self.get_balance(user_id, "bank")
        total = wallet + bank
        InternalCommandExecutor.log_action("Balance Check", f"User {user_id}: Wallet=${wallet}, Bank=${bank}, Total=${total}")
        return InternalCommandResult(True, data={"wallet": wallet, "bank": bank, "total": total})
    
    def internal_deposit(self, user_id, amount):
        """Internal: Deposit money silently"""
        wallet = self.get_balance(user_id, "wallet")
        if amount <= 0 or amount > wallet:
            return InternalCommandResult(False, f"Cannot deposit ${amount}")
        self.update_balance(user_id, -amount, "wallet")
        self.update_balance(user_id, amount, "bank")
        InternalCommandExecutor.log_action("Deposit", f"User {user_id} deposited ${amount}")
        return InternalCommandResult(True, f"Deposited ${amount}", data={"amount": amount})
    
    def internal_withdraw(self, user_id, amount):
        """Internal: Withdraw money silently"""
        bank = self.get_balance(user_id, "bank")
        if amount <= 0 or amount > bank:
            return InternalCommandResult(False, f"Cannot withdraw ${amount}")
        self.update_balance(user_id, -amount, "bank")
        self.update_balance(user_id, amount, "wallet")
        InternalCommandExecutor.log_action("Withdraw", f"User {user_id} withdrew ${amount}")
        return InternalCommandResult(True, f"Withdrew ${amount}", data={"amount": amount})
    
    def internal_rob(self, robber_id, victim_id, reason="", steal_percent=0.4):
        """Internal: Rob money from wallet (NOT bank) silently but log the action"""
        victim_wallet = self.get_balance(victim_id, "wallet")
        if victim_wallet <= 0:
            return InternalCommandResult(False, f"Target has no money to rob")
        
        rob_amount = int(victim_wallet * steal_percent)
        if rob_amount <= 0:
            rob_amount = min(10, victim_wallet)
        
        self.update_balance(victim_id, -rob_amount, "wallet")
        self.update_balance(robber_id, rob_amount, "wallet")
        
        victim = self.bot.get_user(victim_id)
        victim_name = victim.display_name if victim else f"User {victim_id}"
        InternalCommandExecutor.log_rob(victim_name, rob_amount, reason)
        
        return InternalCommandResult(True, f"Robbed ${rob_amount}", data={"amount": rob_amount})

    @commands.command()
    async def shop(self, ctx):
        """List available items."""
        if not self.items:
            return await ctx.send("Shop is empty right now.")

        lines = []
        for item_id, item in self.items.items():
            name = item.get("name", item_id)
            price = item.get("price", 0)
            desc = item.get("description", "No description.")
            lines.append(f"**{name}** (`{item_id}`) - ${price} | {desc}")

        embed = discord.Embed(title="Shop", description="\n".join(lines), color=0xFFD700)
        await ctx.send(embed=embed)

    @commands.command(aliases=["inv"])
    async def inventory(self, ctx):
        """Show your inventory."""
        uid = str(ctx.author.id)
        inv = self.economy_manager.db.get_inventory(uid)
        if not inv:
            return await ctx.send("Your inventory is empty.")

        lines = []
        for item_id, qty in inv.items():
            item = self.items.get(item_id, {})
            name = item.get("name", item_id)
            lines.append(f"{name} (`{item_id}`) x{qty}")

        embed = discord.Embed(title=f"Inventory: {ctx.author.display_name}", description="\n".join(lines), color=0x3498db)
        await ctx.send(embed=embed)

    @commands.command()
    async def buy(self, ctx, item_name: str, quantity: int = 1):
        """Buy an item from the shop."""
        if quantity <= 0:
            return await ctx.send("Quantity must be positive.")

        item_id, item = self.find_item(item_name)
        if not item:
            return await ctx.send("Item not found.")

        price = int(item.get("price", 0))
        total_cost = price * quantity
        wallet = self.get_balance(ctx.author.id, "wallet")
        if wallet < total_cost:
            return await ctx.send("You don't have enough money in your wallet.")

        self.update_balance(ctx.author.id, -total_cost, "wallet")
        uid = str(ctx.author.id)
        new_qty = self.economy_manager.db.update_inventory(uid, item_id, quantity)
        await ctx.send(f"Purchased {quantity}x {item.get('name', item_id)}. You now have {new_qty}.")

    @commands.command()
    async def use(self, ctx, item_name: str):
        """Use a consumable item."""
        item_id, item = self.find_item(item_name)
        if not item:
            return await ctx.send("Item not found.")
        if item.get("type") != "consumable":
            return await ctx.send("This item cannot be used.")

        uid = str(ctx.author.id)
        qty = self.economy_manager.db.get_inventory_item(uid, item_id)
        if qty <= 0:
            return await ctx.send("You don't own that item.")

        buff_id = item.get("buff_id")
        buff_value = float(item.get("buff_value", 0))
        duration_minutes = int(item.get("buff_duration_minutes", 0))
        if not buff_id or duration_minutes <= 0:
            return await ctx.send("This item has no usable effect.")

        self.economy_manager.db.update_inventory(uid, item_id, -1)
        self.economy_manager.db.set_active_buff(uid, buff_id, buff_value, duration_minutes * 60)
        await ctx.send(f"Used {item.get('name', item_id)}. Effect active for {duration_minutes} minutes.")

    @commands.command(aliases=['bal', 'money'])
    async def balance(self, ctx, member: discord.Member = None):
        """Check your wallet and bank balance."""
        member = member or ctx.author
        wallet = self.get_balance(member.id, "wallet")
        bank = self.get_balance(member.id, "bank")
        total = wallet + bank

        embed = discord.Embed(title=f"💸 Balance: {member.display_name}", color=0xFFD700)
        embed.add_field(name="👛 Wallet", value=f"${wallet}", inline=True)
        embed.add_field(name="🏦 Bank", value=f"${bank}", inline=True)
        embed.add_field(name="💰 Total", value=f"${total}", inline=False)
        await ctx.send(embed=embed)

    @commands.command(aliases=['dep'])
    async def deposit(self, ctx, amount: str):
        """Deposit money from your wallet to your bank. Usage: !dep <amount/all>"""
        wallet = self.get_balance(ctx.author.id, "wallet")
        
        if amount.lower() == "all":
            amount_int = wallet
        else:
            try:
                amount_int = int(amount)
            except ValueError:
                await ctx.send("❌ Please enter a valid number.")
                return

        if amount_int <= 0:
            return await ctx.send("❌ Amount must be positive.")
        if amount_int > wallet:
            return await ctx.send("❌ You don't have that much money in your wallet.")

        self.update_balance(ctx.author.id, -amount_int, "wallet")
        self.update_balance(ctx.author.id, amount_int, "bank")
        await ctx.send(f"✅ Deposited **${amount_int}** into your bank.")

    @commands.command(aliases=['with'])
    async def withdraw(self, ctx, amount: str):
        """Withdraw money from your bank to your wallet. Usage: !with <amount/all>"""
        bank = self.get_balance(ctx.author.id, "bank")
        
        if amount.lower() == "all":
            amount_int = bank
        else:
            try:
                amount_int = int(amount)
            except ValueError:
                await ctx.send("❌ Please enter a valid number.")
                return

        if amount_int <= 0:
            return await ctx.send("❌ Amount must be positive.")
        if amount_int > bank:
            return await ctx.send("❌ You don't have that much money in your bank.")

        self.update_balance(ctx.author.id, -amount_int, "bank")
        self.update_balance(ctx.author.id, amount_int, "wallet")
        await ctx.send(f"✅ Withdrew **${amount_int}** from your bank.")

    @commands.command()
    @commands.cooldown(1, 3600, commands.BucketType.user)
    async def work(self, ctx):
        """Work a random job to earn money. Cooldown: 1 hour."""
        earnings = random.randint(50, 250)
        uid = str(ctx.author.id)
        luck_bonus_pct = self.economy_manager.db.get_active_buff(uid, "luck_boost") or 0
        luck_bonus = int(earnings * luck_bonus_pct) if luck_bonus_pct else 0
        total_earnings = earnings + luck_bonus
        self.update_balance(ctx.author.id, total_earnings, "wallet")
        
        jobs = ["Developer", "Pizza Delivery", "Discord Mod", "Uber Driver", "Artist"]
        job = random.choice(jobs)
        
        bonus_text = f" (Luck bonus +${luck_bonus})" if luck_bonus > 0 else ""
        await ctx.send(f"\U0001f477 You worked as a **{job}** and earned **${total_earnings}**!{bonus_text}")

    @commands.command()
    async def pay(self, ctx, member: discord.Member, amount: int):
        """Transfer money to another user or the bot."""
        if member == ctx.author:
            return await ctx.send("❌ You can't pay yourself.")
        if amount <= 0:
            return await ctx.send("❌ Amount must be positive.")
        
        wallet = self.get_balance(ctx.author.id, "wallet")
        if wallet < amount:
            return await ctx.send("❌ You don't have enough money in your wallet.")

        self.update_balance(ctx.author.id, -amount, "wallet")
        self.update_balance(member.id, amount, "wallet")
        
        if member.id == self.bot.user.id:
            self.economy_manager.record_donation(ctx.author.id, amount, self.bot.user.id)
            response = random.choice(self.donation_responses)
            await ctx.send(f"🤝 {response}")
        else:
            await ctx.send(f"💸 You paid **{member.display_name}** ${amount}!")

    @commands.command()
    async def daily(self, ctx):
        """Collect your daily money reward. Resets at 7 AM UTC."""
        now_utc = datetime.now(timezone.utc)
        tz = timezone(timedelta(hours=7))
        now_tz = now_utc.astimezone(tz)
        today = now_tz.date().isoformat()
        yesterday = (now_tz.date() - timedelta(days=1)).isoformat()

        uid = str(ctx.author.id)
        self.economy_manager.check_account(uid)
        
        user_data = self.economy_manager.db.get_user_economy(uid)
        last = user_data.get("last_daily")
        streak = int(user_data.get("daily_streak", 0))

        if last == today:
            next_reset = datetime.combine(now_tz.date() + timedelta(days=1), datetime_time.min, tzinfo=tz)
            remaining = next_reset - now_tz
            total_seconds = int(remaining.total_seconds())
            h, rem = divmod(total_seconds, 3600)
            m, _ = divmod(rem, 60)
            await ctx.send(f"⏳ Come back in **{h}h {m}m** for your daily reward.")
            return

        if last == yesterday:
            streak += 1
        else:
            streak = 1

        base_reward = 100
        streak_bonus = min(200, (streak - 1) * 10)
        luck_bonus_pct = self.economy_manager.db.get_active_buff(uid, "luck_boost") or 0
        luck_bonus = int((base_reward + streak_bonus) * luck_bonus_pct) if luck_bonus_pct else 0
        total_reward = base_reward + streak_bonus + luck_bonus

        self.update_balance(ctx.author.id, total_reward, "wallet")
        self.economy_manager.set_daily_status(uid, today, streak)

        parts = [f"\U0001f4b8 {ctx.author.mention}, you collected your daily **${total_reward}**!"]
        parts.append(f"Streak: **{streak}**")
        if streak_bonus > 0:
            parts.append(f"Streak bonus +${streak_bonus}")
        if luck_bonus > 0:
            parts.append(f"Luck bonus +${luck_bonus}")
        await ctx.send(" | ".join(parts))

    @commands.command()
    @commands.cooldown(1, 3600, commands.BucketType.user)
    async def rob(self, ctx, target: discord.Member):
        """Attempt to steal money from another user. Warning: You might get caught!"""
        if target == ctx.author:
            return await ctx.send("Invalid target.")
        
        uid = str(ctx.author.id)
        robber_wallet = self.get_balance(ctx.author.id, "wallet")
        if robber_wallet < 500: 
            return await ctx.send("You need at least $500 in your wallet to attempt a robbery (bail money).")

        if target.id == self.bot.user.id:
            bot_wallet = self.get_balance(self.bot.user.id, "wallet")
            if bot_wallet < 100: 
                return await ctx.send("They are too poor to rob.")

            buff_value = self.economy_manager.db.get_active_buff(uid, "rob_safety")
            fine_reduction = 0.0
            buff_note = ""
            if buff_value:
                self.economy_manager.db.clear_active_buff(uid, "rob_safety")
                fine_reduction = 0.5
                buff_note = " (Safety buff used)"

            success_chance = min(0.95, 0.5 + float(buff_value or 0))
            success = random.random() < success_chance
            if success:
                percent = random.randint(10, 30) / 100
                stolen = int(bot_wallet * percent)
                self.update_balance(ctx.author.id, stolen, "wallet")
                self.update_balance(self.bot.user.id, -stolen, "wallet")
                await ctx.send(f"\U0001f608 You stole **${stolen}** from the bot's wallet!{buff_note}")
            else:
                fine = min(500, robber_wallet)
                if fine_reduction > 0:
                    fine = max(1, int(fine * (1 - fine_reduction)))
                self.update_balance(ctx.author.id, -fine, "wallet")
                self.update_balance(self.bot.user.id, fine, "wallet")
                
                self.economy_manager.reduce_donation(ctx.author.id, str(self.bot.user.id))

                bot_personality_cog = self.bot.get_cog("BotPersonality")
                msg = random.choice(bot_personality_cog.snark_lines) if bot_personality_cog else "You got caught!"
                await ctx.send(f"\U0001f694 You got caught robbing the bot! You paid a **${fine}** fine.{buff_note}\n**Bot:** {msg}")
            return

        target_wallet = self.get_balance(target.id, "wallet")
        if target_wallet < 100: 
            return await ctx.send("They are too poor to rob.")

        buff_value = self.economy_manager.db.get_active_buff(uid, "rob_safety")
        fine_reduction = 0.0
        buff_note = ""
        if buff_value:
            self.economy_manager.db.clear_active_buff(uid, "rob_safety")
            fine_reduction = 0.5
            buff_note = " (Safety buff used)"

        success_chance = min(0.95, 0.5 + float(buff_value or 0))
        success = random.random() < success_chance
        if success:
            percent = random.randint(10, 30) / 100
            stolen = int(target_wallet * percent)
            self.update_balance(ctx.author.id, stolen, "wallet")
            self.update_balance(target.id, -stolen, "wallet")
            await ctx.send(f"\U0001f608 You stole **${stolen}** from {target.mention}'s wallet!{buff_note}")
        else:
            fine = 500
            if fine_reduction > 0:
                fine = max(1, int(fine * (1 - fine_reduction)))
            self.update_balance(ctx.author.id, -fine, "wallet")
            await ctx.send(f"\U0001f694 You got caught! You paid a **${fine}** fine.{buff_note}")

    @commands.command()
    async def tictactoe(self, ctx, opponent: discord.Member):
        """Challenge another user to a game of Tic-Tac-Toe."""
        if opponent.bot or opponent == ctx.author: return await ctx.send("Invalid opponent.")
        await ctx.send(f"Tic Tac Toe: {ctx.author.mention} vs {opponent.mention}", view=TicTacToeView(ctx.author, opponent))

async def setup(bot):
    await bot.add_cog(Games(bot))
