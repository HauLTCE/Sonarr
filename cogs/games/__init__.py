import discord
from discord.ext import commands
import random
import asyncio

from utils.economy_helpers import get_balance, update_wallet, spend_wallet, record_gamble
from cogs.games.utils import validate_bet, check_channel
from cogs.games.highlow import start_highlow

from cogs.games.blackjack import start_blackjack
from cogs.games.roulette import start_roulette
from cogs.games.crash import start_crash
from cogs.games.mines import start_mines
from cogs.games.rob import start_rob
from cogs.games.duel import start_duel
from cogs.games.tictactoe import start_ttt
from cogs.games.connect_four import start_c4
from cogs.games.heist import start_heist
from cogs.games.arena import start_arena
from cogs.games.word_chain import start_wordchain

class Games(commands.Cog):
    """Casino and gambling games."""
    
    def __init__(self, bot):
        self.bot = bot
        self.active_games = set()

    async def cog_check(self, ctx):
        """Enforce games channel for all commands in this cog."""
        return await check_channel(ctx, "games_channel")

    @commands.command(aliases=['cf'])
    async def coinflip(self, ctx, bet: int, choice_or_user: str = None):
        """Flip a coin — 50/50 odds, 1.9x payout. Bet 10-10,000 🪙.
        PvE: `!coinflip 100 heads` or `!cf 100 tails`
        PvP: `!coinflip 100 @user` — winner takes 95% of the pot (5% tax).
        Accepts h/t as shorthand for heads/tails."""
        user_id = ctx.author.id
        
        if user_id in self.active_games:
            await ctx.send("You're already in a game. Finish that one first.")
            return
            
        # PvP Mode
        if ctx.message.mentions:
            target = ctx.message.mentions[0]
            if target.id == user_id:
                await ctx.send("You can't duel yourself.")
                return
            if target.bot:
                await ctx.send("Bots don't have money.")
                return
            # M2: don't let a challenge pull someone who's already mid-game.
            if target.id in self.active_games:
                await ctx.send(f"{target.display_name} is already in a game.")
                return
                
            if not await validate_bet(ctx, bet, max_bet=10000):
                return
                
            bal_target = get_balance(target.id)
            if bal_target['wallet'] < bet:
                await ctx.send(f"{target.display_name} doesn't have enough coins.")
                return
                
            self.active_games.add(user_id)
            self.active_games.add(target.id)
            
            try:
                # M2: bets are NOT deducted here. Charging before consent locks
                # the target's coins for the whole 30s window (grief vector), so
                # we defer the deduction until the challenge is actually accepted.
                class PvPView(discord.ui.View):
                    def __init__(self):
                        super().__init__(timeout=30)
                        self.accepted = None
                        
                    @discord.ui.button(label="Accept", style=discord.ButtonStyle.green, emoji="✅")
                    async def accept(self, interaction: discord.Interaction, button: discord.ui.Button):
                        if interaction.user.id != target.id:
                            await interaction.response.send_message("You are not challenged.", ephemeral=True)
                            return
                        self.accepted = True
                        await interaction.response.edit_message(view=None)
                        self.stop()
                        
                    @discord.ui.button(label="Decline", style=discord.ButtonStyle.red, emoji="❌")
                    async def decline(self, interaction: discord.Interaction, button: discord.ui.Button):
                        if interaction.user.id != target.id:
                            await interaction.response.send_message("You are not challenged.", ephemeral=True)
                            return
                        self.accepted = False
                        await interaction.response.edit_message(view=None)
                        self.stop()

                embed = discord.Embed(title="⚔️ Coinflip Challenge", color=0x3498DB)
                embed.description = (
                    f"{ctx.author.mention} challenges {target.mention}!\n"
                    f"Bet: {bet:,} 🪙 each (Pot: {bet*2:,})\n\n"
                    f"{target.mention} has 30 seconds to respond."
                )
                
                view = PvPView()
                msg = await ctx.send(embed=embed, view=view)
                await view.wait()
                
                if view.accepted is None:
                    await msg.edit(content="Challenge timed out.", embed=None)
                    return
                    
                if not view.accepted:
                    await msg.edit(content=f"{target.display_name} declined the challenge.", embed=None)
                    return

                # Consent given — charge both atomically now. If either can no
                # longer cover the bet, refund whoever was charged and abort.
                if not spend_wallet(user_id, bet):
                    await msg.edit(content="You no longer have enough coins. Challenge cancelled.", embed=None)
                    return
                if not spend_wallet(target.id, bet):
                    update_wallet(user_id, bet)
                    await msg.edit(content=f"{target.display_name} no longer has enough coins. Challenge cancelled.", embed=None)
                    return

                winner = random.choice([ctx.author, target])
                loser = target if winner == ctx.author else ctx.author
                
                pot = bet * 2
                tax = int(pot * 0.05)
                payout = pot - tax
                
                update_wallet(winner.id, payout)
                
                record_gamble(winner.id, bet, payout - bet)
                record_gamble(loser.id, bet, -bet)
                
                res_embed = discord.Embed(title="⚔️ Coinflip Result", color=0xF1C40F)
                res_embed.description = (
                    f"Result: The coin landed on... something!\n\n"
                    f"🏆 {winner.mention} wins **{payout:,} 🪙**!\n"
                    f"💀 {loser.mention} lost {bet:,} 🪙."
                )
                # Money is already settled above; a failed edit must not refund.
                try:
                    await msg.edit(embed=res_embed)
                except discord.HTTPException:
                    pass
            finally:
                self.active_games.discard(user_id)
                self.active_games.discard(target.id)
                
            return

        # PvE Mode
        if choice_or_user is None:
            await ctx.send("Please specify heads or tails. e.g., `!coinflip 100 heads`")
            return
            
        choice = choice_or_user.lower()
        if choice not in ['heads', 'tails', 'h', 't']:
            await ctx.send("Choose heads or tails.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=10000):
            return
            
        self.active_games.add(user_id)

        # Atomic deduction: fail instead of clamping to 0.
        if not spend_wallet(user_id, bet):
            self.active_games.discard(user_id)
            await ctx.send("You don't have enough coins in your wallet for that bet.")
            return

        payout_done = False
        try:
            bot_choice = random.choice(['heads', 'tails'])
            player_choice_full = 'heads' if choice in ['heads', 'h'] else 'tails'
            
            embed = discord.Embed(title="🪙 Coinflip", color=0xF1C40F)
            embed.description = (
                f"Bet: {bet:,} 🪙\n"
                f"You called: {player_choice_full.upper()}\n"
                f"Result: [coin spinning animation...]"
            )
            msg = await ctx.send(embed=embed)
            
            await asyncio.sleep(1)
            
            if bot_choice == player_choice_full:
                payout = int(bet * 1.90)
                update_wallet(user_id, payout)
                record_gamble(user_id, bet, payout - bet)
                embed.description = (
                    f"Bet: {bet:,} 🪙\n"
                    f"You called: {player_choice_full.upper()}\n"
                    f"... {bot_choice.upper()}! You won {payout:,} 🪙!"
                )
            else:
                record_gamble(user_id, bet, -bet)
                embed.description = (
                    f"Bet: {bet:,} 🪙\n"
                    f"You called: {player_choice_full.upper()}\n"
                    f"... {bot_choice.upper()}. You lost {bet:,} 🪙."
                )

            # Outcome settled — a failed message edit past here must not refund.
            payout_done = True
            await msg.edit(embed=embed)
            
        except Exception as e:
            if not payout_done:
                update_wallet(user_id, bet)
                await ctx.send("An error occurred. Bet refunded.")
            raise e
        finally:
            self.active_games.discard(user_id)

    @commands.command(aliases=['hl'])
    async def highlow(self, ctx, bet: int):
        """Guess if the next number is higher or lower. Bet 10-5,000 🪙.
        A random number (1-100) is revealed. Guess if the hidden number is higher or lower.
        Correct guesses multiply your bet based on how unlikely the guess was.
        Equal numbers re-roll automatically. Usage: `!highlow 100` or `!hl 100`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_highlow(self, ctx, bet)



    @commands.command(aliases=['bj'])
    async def blackjack(self, ctx, bet: int):
        """Classic 21 — beat the dealer without going over. Bet 10-5,000 🪙.
        Hit to draw, Stand to hold, Double Down to double your bet and draw one card.
        Dealer stands on 17. Blackjack (21 on first 2 cards) pays 2.5x.
        Timeout refunds your bet. Usage: `!blackjack 100` or `!bj 100`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_blackjack(self, ctx, bet)

    @commands.command(aliases=['rl'])
    async def roulette(self, ctx, bet: int, choice: str = None):
        """Spin the roulette wheel — European rules (0-36). Bet 10-5,000 🪙.
        Bet on: `red`/`black` (2x), `odd`/`even` (2x), `low`/`high` aka `1-18`/`19-36` (2x),
        a dozen `1st`/`2nd`/`3rd` (3x), or a single number 0-36 (36x).
        Usage: `!roulette 100 red` or `!rl 100 17`"""
        if choice is None:
            await ctx.send("Please specify a choice. e.g., `!roulette 100 red` or `!roulette 100 17`")
            return
            
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_roulette(self, ctx, bet, choice)

    @commands.command()
    async def crash(self, ctx, bet: int):
        """Ride the multiplier — cash out before it crashes. Bet 10-5,000 🪙.
        The multiplier rises from 1.0x. Cash out anytime to lock in your payout.
        If it crashes before you cash out, you lose everything.
        Higher multipliers = bigger risk. House edge is ~4%. Usage: `!crash 100`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_crash(self, ctx, bet)

    @commands.command()
    async def mines(self, ctx, bet: int, mines: int):
        """Navigate a 5x5 grid of hidden tiles — avoid the mines. Bet 10-5,000 🪙.
        Choose 1-20 mines. More mines = higher multiplier per safe tile revealed.
        Cash out anytime to keep your winnings, or hit a mine and lose it all.
        Usage: `!mines 100 5` (100 coin bet, 5 mines)"""
        if not (1 <= mines <= 20):
            await ctx.send("You must choose between 1 and 20 mines.")
            return
            
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_mines(self, ctx, bet, mines)

    @commands.command()
    async def rob(self, ctx, target: discord.Member):
        """Attempt to pickpocket another user's wallet. No bet required.
        Click the button in time for a better success chance. Steal 15-25% of their wallet.
        Fail and you pay a fine (up to 150 🪙, capped at your balance). 4-hour cooldown.
        Target must have at least 200 🪙. Usage: `!rob @user`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game. Finish that first.")
            return
            
        await start_rob(self, ctx, target)

    @commands.command()
    async def duel(self, ctx, target: discord.Member, bet: int):
        """PvP Russian roulette — take turns pulling the trigger. Bet 10-5,000 🪙.
        Both players put up the same bet. Take turns — one chamber is loaded.
        The loser's bet goes to the winner (minus 5% tax). 30s to accept.
        Usage: `!duel @user 500`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if target.id == ctx.author.id:
            await ctx.send("You can't duel yourself.")
            return
            
        if target.bot:
            await ctx.send("Bots don't play.")
            return

        if target.id in self.active_games:
            await ctx.send(f"{target.display_name} is already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        bal_target = get_balance(target.id)
        if bal_target['wallet'] < bet:
            await ctx.send(f"{target.display_name} doesn't have enough coins.")
            return
            
        await start_duel(self, ctx, target, bet)

    @commands.command(aliases=['tictactoe'])
    async def ttt(self, ctx, target: discord.Member, bet: int = 0):
        """Classic Tic-Tac-Toe — play for fun or for coins. Bet 0-5,000 🪙.
        Challenge another user to a 3x3 grid battle. First to 3 in a row wins.
        Bet is optional — set to 0 for a casual game.
        Usage: `!ttt @user 100` or `!tictactoe @user`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if target.id == ctx.author.id:
            await ctx.send("You can't play yourself.")
            return
            
        if target.bot:
            await ctx.send("Bots don't play.")
            return

        if target.id in self.active_games:
            await ctx.send(f"{target.display_name} is already in a game.")
            return
            
        if bet > 0:
            if not await validate_bet(ctx, bet, max_bet=5000):
                return
                
            bal_target = get_balance(target.id)
            if bal_target['wallet'] < bet:
                await ctx.send(f"{target.display_name} doesn't have enough coins.")
                return
                
        await start_ttt(self, ctx, target, bet)

    @commands.command(aliases=['c4'])
    async def connect_four(self, ctx, target: discord.Member, bet: int = 0):
        """Drop discs into a 7x6 grid — first to connect 4 wins. Bet 0-5,000 🪙.
        Take turns dropping discs into columns. Connect 4 horizontally, vertically, or diagonally.
        Bet is optional — set to 0 for a casual game.
        Usage: `!connect_four @user 100` or `!c4 @user`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if target.id == ctx.author.id:
            await ctx.send("You can't play yourself.")
            return
            
        if target.bot:
            await ctx.send("Bots don't play.")
            return

        if target.id in self.active_games:
            await ctx.send(f"{target.display_name} is already in a game.")
            return
            
        if bet > 0:
            if not await validate_bet(ctx, bet, max_bet=5000):
                return
                
            bal_target = get_balance(target.id)
            if bal_target['wallet'] < bet:
                await ctx.send(f"{target.display_name} doesn't have enough coins.")
                return
            
        await start_c4(self, ctx, target, bet)

    @commands.command()
    async def heist(self, ctx, action_or_amount: str = None):
        """Organize a group heist — more players = better odds. Ante 10-5,000 🪙.
        Start a heist lobby with an ante. Other players can join for 60 seconds.
        Success chance: 30% base + 10% per extra player (max 80%).
        Payout scales with risk. Everyone loses their ante on failure.
        Usage: `!heist 500`"""
        if action_or_amount is None:
            await ctx.send("Specify an ante amount (e.g. `!heist 500`).")
            return
            
        if action_or_amount.lower() == 'start':
            # This is handled within the view, we don't need a separate command invocation
            await ctx.send("Click the 'Force Start' button on the heist message instead.")
            return
            
        try:
            ante = int(action_or_amount)
        except ValueError:
            await ctx.send("Specify a valid ante amount.")
            return
            
        if not await validate_bet(ctx, ante, max_bet=5000):
            return
            
        await start_heist(self, ctx, ante)

    @commands.command()
    async def arena(self, ctx, bet: int):
        """PvP combat arena — queue up and get matched against another player betting
        the same amount. Bet 10-5,000 🪙. Each round, pick Attack (🗡️), Defend (🛡️),
        or Special (⚡): Defend blocks and counters Attack; Special hits hard but can miss
        and be interrupted. Fight until someone hits 0 HP. Winner takes the pot (10% tax).
        Queue cancels after 60s with no opponent. Usage: `!arena 100`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        if not await validate_bet(ctx, bet, max_bet=5000):
            return
            
        await start_arena(self, ctx, bet)

    @commands.command()
    async def wordchain(self, ctx):
        """Multiplayer word chain — each word must start with the last letter of the previous word.
        Free to play (no bet). Anyone in the channel can join.
        10 second timer per turn. Repeat words or invalid words = elimination.
        Last player standing wins bragging rights. Usage: `!wordchain`"""
        if ctx.author.id in self.active_games:
            await ctx.send("You're already in a game.")
            return
            
        await start_wordchain(self, ctx)

async def setup(bot):
    await bot.add_cog(Games(bot))
