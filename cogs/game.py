import discord
from discord.ext import commands
import random
import logging
from datetime import datetime, timezone, timedelta, time as datetime_time

from .views import TicTacToeView
from utils.economy import EconomyManager
from utils.internal_commands import InternalCommandResult, InternalCommandExecutor

logger = logging.getLogger("bot")

SHOP_FILE = "shop.json"
CREATOR_ID = "chito8196"

class Games(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()
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
        self.update_balance(ctx.author.id, earnings, "wallet")
        
        jobs = ["Developer", "Pizza Delivery", "Discord Mod", "Uber Driver", "Artist"]
        job = random.choice(jobs)
        
        await ctx.send(f"👷 You worked as a **{job}** and earned **${earnings}**!")

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

        uid = str(ctx.author.id)
        self.economy_manager.check_account(uid)
        
        user_data = self.economy_manager.economy[uid]
        last = user_data.get("last_daily")

        if last == today:
            next_reset = datetime.combine(now_tz.date() + timedelta(days=1), datetime_time.min, tzinfo=tz)
            remaining = next_reset - now_tz
            total_seconds = int(remaining.total_seconds())
            h, rem = divmod(total_seconds, 3600)
            m, _ = divmod(rem, 60)
            await ctx.send(f"⏳ Come back in **{h}h {m}m** for your daily reward.")
            return

        self.update_balance(ctx.author.id, 100, "wallet")
        self.economy_manager.economy[uid]["last_daily"] = today
        self.economy_manager.force_save()
        await ctx.send(f"💸 {ctx.author.mention}, you collected your daily **$100**!")

    @commands.command()
    @commands.cooldown(1, 3600, commands.BucketType.user)
    async def rob(self, ctx, target: discord.Member):
        """Attempt to steal money from another user. Warning: You might get caught!"""
        if target == ctx.author:
            return await ctx.send("Invalid target.")
        
        robber_wallet = self.get_balance(ctx.author.id, "wallet")
        if robber_wallet < 500: 
            return await ctx.send("You need at least $500 in your wallet to attempt a robbery (bail money).")

        if target.id == self.bot.user.id:
            bot_wallet = self.get_balance(self.bot.user.id, "wallet")
            if bot_wallet < 100: 
                return await ctx.send("They are too poor to rob.")

            success = random.choice([True, False])
            if success:
                percent = random.randint(10, 30) / 100
                stolen = int(bot_wallet * percent)
                self.update_balance(ctx.author.id, stolen, "wallet")
                self.update_balance(self.bot.user.id, -stolen, "wallet")
                await ctx.send(f"😈 You stole **${stolen}** from the bot's wallet!")
            else:
                fine = min(500, robber_wallet)
                self.update_balance(ctx.author.id, -fine, "wallet")
                self.update_balance(self.bot.user.id, fine, "wallet")
                
                self.economy_manager.reduce_donation(ctx.author.id, str(self.bot.user.id))

                favor_cog = self.bot.get_cog("Favor")
                msg = random.choice(favor_cog.snark_lines) if favor_cog else "You got caught!"
                await ctx.send(f"🚔 You got caught robbing the bot! You paid a **${fine}** fine.\n**Bot:** {msg}")
            return

        target_wallet = self.get_balance(target.id, "wallet")
        if target_wallet < 100: 
            return await ctx.send("They are too poor to rob.")

        success = random.choice([True, False])
        if success:
            percent = random.randint(10, 30) / 100
            stolen = int(target_wallet * percent)
            self.update_balance(ctx.author.id, stolen, "wallet")
            self.update_balance(target.id, -stolen, "wallet")
            await ctx.send(f"😈 You stole **${stolen}** from {target.mention}'s wallet!")
        else:
            fine = 500
            self.update_balance(ctx.author.id, -fine, "wallet")
            await ctx.send(f"🚔 You got caught! You paid a **${fine}** fine.")

    @commands.command()
    async def tictactoe(self, ctx, opponent: discord.Member):
        """Challenge another user to a game of Tic-Tac-Toe."""
        if opponent.bot or opponent == ctx.author: return await ctx.send("Invalid opponent.")
        await ctx.send(f"Tic Tac Toe: {ctx.author.mention} vs {opponent.mention}", view=TicTacToeView(ctx.author, opponent))

async def setup(bot):
    await bot.add_cog(Games(bot))