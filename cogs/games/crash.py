import discord
import random
import asyncio
from utils.economy_helpers import update_wallet, record_gamble

def generate_crash_point():
    house_edge = 0.04
    r = random.random()
    if r < house_edge:
        return 1.00
    crash = 0.96 / (1 - r)
    return round(crash, 2)

class CrashView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int):
        super().__init__(timeout=60)
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
    try:
        update_wallet(ctx.author.id, -bet)
        
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
            cog.active_games.discard(ctx.author.id)
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
        update_wallet(ctx.author.id, bet)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
    finally:
        cog.active_games.discard(ctx.author.id)
