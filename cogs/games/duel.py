import discord
import random
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

class DuelGameView(discord.ui.View):
    def __init__(self, cog, ctx, target, bet, pot):
        super().__init__(timeout=30)
        self.cog = cog
        self.ctx = ctx
        self.target = target
        self.bet = bet
        self.pot = pot
        self.chamber = random.randint(1, 6)
        self.current_round = 1
        
        self.p1 = ctx.author
        self.p2 = target
        
        self.current_turn = self.p1
        self.is_over = False
        self.msg = None
        
    def generate_embed(self):
        embed = discord.Embed(title="💀 Russian Roulette", color=0x3498DB)
        
        chambers = ""
        for i in range(1, 7):
            if i < self.current_round:
                chambers += "[💨] "
            else:
                chambers += "[❓] "
                
        embed.description = (
            f"Pot: {self.pot:,} 🪙\n\n"
            f"🔫 Round {self.current_round} of 6\n"
            f"Chamber: {chambers}\n\n"
            f"{self.current_turn.mention}'s turn to pull the trigger..."
        )
        return embed

    async def do_pull(self, interaction=None):
        if self.is_over:
            return
            
        is_dead = (self.current_round == self.chamber)
        
        if is_dead:
            self.is_over = True
            
            winner = self.p2 if self.current_turn == self.p1 else self.p1
            loser = self.current_turn
            
            tax = int(self.pot * 0.05)
            payout = self.pot - tax
            
            update_wallet(winner.id, payout)
            record_gamble(winner.id, self.bet, payout - self.bet)
            record_gamble(loser.id, self.bet, -self.bet)
            
            embed = discord.Embed(title=f"💀 BANG! {loser.display_name} is dead.", color=0xE74C3C)
            
            chambers = ""
            for i in range(1, 7):
                if i < self.current_round:
                    chambers += "[💨] "
                elif i == self.current_round:
                    chambers += "[💥] "
                else:
                    chambers += "[❓] "
                    
            embed.description = (
                f"🏆 {winner.mention} wins {payout:,} 🪙!\n"
                f"({tax:,} 🪙 house tax deducted)\n\n"
                f"Final: {chambers}"
            )
            
            for child in self.children:
                child.disabled = True
                
            if interaction:
                await interaction.response.edit_message(embed=embed, view=self)
            elif self.msg:
                await self.msg.edit(embed=embed, view=self)
                
            self.cog.active_games.discard(self.p1.id)
            self.cog.active_games.discard(self.p2.id)
            self.stop()
        else:
            self.current_round += 1
            self.current_turn = self.p2 if self.current_turn == self.p1 else self.p1
            
            embed = self.generate_embed()
            if interaction:
                await interaction.response.edit_message(embed=embed, view=self)
            elif self.msg:
                await self.msg.edit(embed=embed, view=self)
                
            # reset timeout
            self.timeout = 30.0

    @discord.ui.button(label="🔫 Pull Trigger", style=discord.ButtonStyle.danger)
    async def pull(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.current_turn.id:
            await interaction.response.send_message("It's not your turn!", ephemeral=True)
            return
            
        await self.do_pull(interaction)

    async def on_timeout(self):
        if not self.is_over:
            await self.do_pull(None)

class DuelAcceptView(discord.ui.View):
    def __init__(self, target):
        super().__init__(timeout=60)
        self.target = target
        self.accepted = None
        
    @discord.ui.button(label="Accept", style=discord.ButtonStyle.green, emoji="✅")
    async def accept(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.target.id:
            await interaction.response.send_message("Not your challenge.", ephemeral=True)
            return
        self.accepted = True
        await interaction.response.edit_message(view=None)
        self.stop()
        
    @discord.ui.button(label="Decline", style=discord.ButtonStyle.red, emoji="❌")
    async def decline(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.target.id:
            await interaction.response.send_message("Not your challenge.", ephemeral=True)
            return
        self.accepted = False
        await interaction.response.edit_message(view=None)
        self.stop()

async def start_duel(cog, ctx, target, bet: int):
    cog.active_games.add(ctx.author.id)
    cog.active_games.add(target.id)

    def _release():
        cog.active_games.discard(ctx.author.id)
        cog.active_games.discard(target.id)

    try:
        # M2: don't charge until the challenge is accepted — no locked coins
        # during the 60s window.
        embed = discord.Embed(title="💀 Russian Roulette Challenge", color=0x3498DB)
        embed.description = (
            f"{ctx.author.mention} challenges {target.mention}!\n"
            f"Bet: {bet:,} 🪙 each (Pot: {bet*2:,} 🪙)\n\n"
            f"{target.mention} has 60 seconds to respond."
        )
        
        accept_view = DuelAcceptView(target)
        msg = await ctx.send(embed=embed, view=accept_view)
        
        await accept_view.wait()
        
        if accept_view.accepted is None:
            await msg.edit(content="Challenge timed out.", embed=None)
            _release()
            return
            
        if not accept_view.accepted:
            await msg.edit(content=f"{target.display_name} declined.", embed=None)
            _release()
            return

        # Consent given — charge both atomically (M1). Abort + refund if either
        # can no longer cover the bet.
        if not spend_wallet(ctx.author.id, bet):
            await msg.edit(content="You no longer have enough coins. Duel cancelled.", embed=None)
            _release()
            return
        if not spend_wallet(target.id, bet):
            update_wallet(ctx.author.id, bet)
            await msg.edit(content=f"{target.display_name} no longer has enough coins. Duel cancelled.", embed=None)
            _release()
            return

        # Both charged. Hand off to the game view (which owns active_games and the
        # win/lose payout from here). If we can't even show the board, refund both.
        try:
            game_view = DuelGameView(cog, ctx, target, bet, bet*2)
            game_view.msg = msg
            await msg.edit(embed=game_view.generate_embed(), view=game_view)
        except Exception:
            update_wallet(ctx.author.id, bet)
            update_wallet(target.id, bet)
            _release()
            raise
        
    except Exception as e:
        # Pre-charge failure (nothing deducted yet) — just release the flags.
        _release()
        await ctx.send("An error occurred.")
        raise e
