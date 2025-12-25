import discord
from discord.ext import commands
import random
import asyncio
import logging
from collections import Counter

from .views import BlackjackView, DuelView
from utils.economy import EconomyManager

logger = logging.getLogger("bot")

class Gambling(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()

    def get_balance(self, user_id, location="wallet"):
        return self.economy_manager.get_balance(user_id, location)

    def update_balance(self, user_id, amount, location="wallet"):
        self.economy_manager.update_balance(user_id, amount, location)

    @commands.command()
    async def slots(self, ctx, *args):
        """Play the slot machine. Usage: !slots <amount> or !slots advanced <amount>."""
        if not args:
            return await ctx.send("Usage: `!slots <amount>` or `!slots advanced <amount>`")

        mode = 'classic'
        try:
            if isinstance(args[0], str) and args[0].lower() in ('advanced', 'adv'):
                if len(args) < 2:
                    return await ctx.send("Usage: `!slots advanced <amount>`")
                amount = int(args[1])
                mode = 'advanced'
            else:
                amount = int(args[0])
        except ValueError:
            return await ctx.send("❌ Please enter a valid number for the bet.")

        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0:
            return await ctx.send("Bet must be positive.")
        if amount > bal:
            return await ctx.send("You don't have enough money in your wallet!")

        self.update_balance(ctx.author.id, -amount, "wallet")

        if mode == 'classic':
            emojis = ["🍎", "🍊", "🍇", "🍒", "💎"]
            a, b, c = random.choice(emojis), random.choice(emojis), random.choice(emojis)
            result_msg = f"🎰 | {a} | {b} | {c} | 🎰"

            if a == b == c:
                winnings = amount * 5
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nJACKPOT!! 🚨 You won **${winnings}**!")
            elif a == b or b == c or a == c:
                winnings = amount * 2
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nNice! Match 2. You won **${winnings}**!")
            else:
                await ctx.send(f"{result_msg}\nNo match. You lost ${amount}.")
        else:
            emojis = ["🍎", "🍊", "🍇", "🍒", "💎", "🍋", "🍉", "⭐", "🔔", "7️⃣"]
            reels = [random.choice(emojis) for _ in range(5)]
            result_msg = "🎰 | " + " | ".join(reels) + " | 🎰"
            counts = Counter(reels)
            most_common_symbol, count = counts.most_common(1)[0]
            multipliers = {5: 12, 4: 5, 3: 1.5, 2: 0.5}
            multiplier = multipliers.get(count, 0)

            if multiplier > 0:
                base_winnings = int(amount * multiplier)
                bonus = 0
                if '💎' in reels and multiplier < multipliers[5]:
                    bonus = int(base_winnings * 0.03)
                winnings = base_winnings + bonus
                if winnings > amount * 60: winnings = amount * 60
                self.update_balance(ctx.author.id, winnings, "wallet")
                await ctx.send(f"{result_msg}\nYou matched **{count}x {most_common_symbol}**! You won **${winnings}** (x{multiplier})")
            else:
                await ctx.send(f"{result_msg}\nNo significant match. You lost ${amount}.")

    @commands.command(aliases=['bj'])
    async def blackjack(self, ctx, amount: int):
        """Play a game of Blackjack against the dealer."""
        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have enough money.")
        view = BlackjackView(self, ctx, amount)
        embed = discord.Embed(title="🃏 Blackjack", description=f"**Your Hand:** {view.player_hand} (Score: {view.calculate_score(view.player_hand)})\n**Dealer Hand:** [{view.dealer_hand[0]}, ?]", color=0x3498db)
        embed.set_footer(text=f"Bet: ${amount}")
        await ctx.send(embed=embed, view=view)

    @commands.command()
    async def roulette(self, ctx, amount: int, choice: str):
        """Bet on Roulette. Options: red, black, odd, even, or number 0-36."""
        bal = self.get_balance(ctx.author.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have enough money.")
        choice = choice.lower()
        valid_choices = ["red", "black", "odd", "even"]
        is_number = False
        try:
            val = int(choice)
            if 0 <= val <= 36: is_number = True
        except ValueError: pass

        if not is_number and choice not in valid_choices:
            return await ctx.send("Invalid choice! Use: red, black, odd, even, or 0-36")

        self.update_balance(ctx.author.id, -amount, "wallet")
        result = random.randint(0, 36)
        red_nums = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36]
        color = "green"
        if result in red_nums: color = "red"
        elif result != 0: color = "black"

        msg = f"🎡 Result: **{result} ({color})**\n"
        winnings = 0
        if is_number and int(choice) == result: winnings = amount * 35
        elif choice == "red" and color == "red": winnings = amount * 2
        elif choice == "black" and color == "black": winnings = amount * 2
        elif choice == "odd" and result != 0 and result % 2 != 0: winnings = amount * 2
        elif choice == "even" and result != 0 and result % 2 == 0: winnings = amount * 2

        if winnings > 0:
            self.update_balance(ctx.author.id, winnings, "wallet")
            await ctx.send(msg + f"🎉 You won **${winnings}**!")
        else:
            await ctx.send(msg + f"❌ You lost ${amount}.")

    @commands.command()
    async def duel(self, ctx, opponent: discord.Member, amount: int):
        """Challenge a user to a wild west duel for cash."""
        bal = self.get_balance(ctx.author.id, "wallet")
        opp_bal = self.get_balance(opponent.id, "wallet")
        if amount <= 0: return await ctx.send("Bet must be positive.")
        if amount > bal: return await ctx.send("You don't have the money.")
        if amount > opp_bal: return await ctx.send(f"{opponent.display_name} doesn't have the money.")
        if opponent.bot or opponent == ctx.author: return await ctx.send("Invalid opponent.")
        self.update_balance(ctx.author.id, -amount, "wallet")
        self.update_balance(opponent.id, -amount, "wallet")
        view = DuelView(self, ctx, opponent, amount)
        await ctx.send(f"🔫 **Duel Started!** {ctx.author.mention} vs {opponent.mention}\nPot: **${amount * 2}**\n{ctx.author.mention} goes first.", view=view)

async def setup(bot):
    await bot.add_cog(Gambling(bot))
