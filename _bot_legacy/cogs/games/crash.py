import discord
import random
import asyncio
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

def generate_crash_point():
    house_edge = 0.04
    r = random.random()
    if r < house_edge:
        return 1.00
    crash = 0.96 / (1 - r)
    return round(crash, 2)

class CrashView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int):
        # No fixed timeout: the game loop (start_crash) owns the lifecycle and
        # stops the view when the round ends. A fixed 60s timeout used to kill
        # the Cash Out button mid-game for high crash points (the 1.5s increment
        # loop outlives 60s), leaving a winning game uncashable.
        super().__init__(timeout=None)
        self.cog = cog
        self.ctx = ctx
        self.bet = bet
        self.cashed_out = False
        self.cashout_multiplier = 0.0
        
    @discord.ui.button(label="💰 Cash Out", style=discord.ButtonStyle.success, custom_id="cashout")
    async def cashout(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        self.cashed_out = True
        button.disabled = True
        await interaction.response.edit_message(view=self)
        self.stop()

async def start_crash(cog, ctx, bet: int):
    cog.active_games.add(ctx.author.id)

    # Atomic deduction (M1): fail instead of clamping to 0.
    if not spend_wallet(ctx.author.id, bet):
        cog.active_games.discard(ctx.author.id)
        await ctx.send("You don't have enough coins in your wallet for that bet.")
        return

    # Guards the broad refund below so a failure after the outcome is decided
    # can't refund on top of a payout / recorded loss.
    settled = False
    try:
        crash_point = generate_crash_point()
        
        view = CrashView(cog, ctx, bet)
        
        embed = discord.Embed(title="📈 Crash", color=0x3498DB)
        embed.description = (
            f"Bet: {bet:,} 🪙\n\n"
            f"      1.00x\n"
            f" ══════════════▶\n\n"
            f"Cash out now: {bet:,} 🪙"
        )
        
        msg = await ctx.send(embed=embed, view=view)
        
        if crash_point == 1.00:
            settled = True
            view.stop()
            for child in view.children:
                child.disabled = True
            embed.color = 0xE74C3C
            embed.description = (
                f"Bet: {bet:,} 🪙\n\n"
                f"   💥 Crashed at 1.00x 💥\n\n"
                f"Instant crash! You lost {bet:,} 🪙."
            )
            record_gamble(ctx.author.id, bet, -bet)
            await msg.edit(embed=embed, view=view)
            return
            
        current_multiplier = 1.00
        
        while current_multiplier < crash_point and not view.cashed_out:
            if current_multiplier < 2.00:
                current_multiplier += 0.05
            elif current_multiplier < 5.00:
                current_multiplier += 0.10
            elif current_multiplier < 10.00:
                current_multiplier += 0.25
            else:
                current_multiplier += 0.50
                
            current_multiplier = round(current_multiplier, 2)
            
            if current_multiplier > crash_point:
                current_multiplier = crash_point
                
            if view.cashed_out:
                break
                
            embed.description = (
                f"Bet: {bet:,} 🪙\n\n"
                f"      {current_multiplier:.2f}x\n"
                f" ══════════════▶\n\n"
                f"Cash out now: {int(bet * current_multiplier):,} 🪙"
            )
            try:
                await msg.edit(embed=embed, view=view)
            except discord.HTTPException:
                pass # ignore rare limits, loop continues
                
            await asyncio.sleep(1.5)

        # Outcome decided (crashed or cashed out) — settle the round.
        settled = True
        view.stop()
        if view.cashed_out:
            view.cashout_multiplier = current_multiplier
            winnings = int(bet * current_multiplier)
            update_wallet(ctx.author.id, winnings)
            record_gamble(ctx.author.id, bet, winnings - bet)
            
            embed.color = 0x2ECC71
            embed.description = (
                f"Bet: {bet:,} 🪙\n\n"
                f"      {current_multiplier:.2f}x\n\n"
                f"✅ You cashed out at {current_multiplier:.2f}x!\n"
                f"Winnings: {winnings:,} 🪙"
            )
        else:
            record_gamble(ctx.author.id, bet, -bet)
            embed.color = 0xE74C3C
            embed.description = (
                f"Bet: {bet:,} 🪙\n\n"
                f"   💥 Crashed at {crash_point:.2f}x 💥\n\n"
                f"You didn't cash out in time.\n"
                f"You lost {bet:,} 🪙."
            )
            
        for child in view.children:
            child.disabled = True
            
        await msg.edit(embed=embed, view=view)
        
    except Exception as e:
        # Only refund if the round never reached settlement.
        if not settled:
            update_wallet(ctx.author.id, bet)
            await ctx.send("An error occurred. Bet refunded.")
        raise e
    finally:
        cog.active_games.discard(ctx.author.id)
