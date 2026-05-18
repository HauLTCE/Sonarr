import discord
import random
import asyncio
import time
from utils.database import db
from utils.economy_helpers import get_balance, update_wallet

class RobView(discord.ui.View):
    def __init__(self, ctx, target):
        super().__init__(timeout=3.0)
        self.ctx = ctx
        self.target = target
        self.clicked = False
        
    @discord.ui.button(label="🏃 GRAB IT", style=discord.ButtonStyle.success)
    async def grab(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your robbery.", ephemeral=True)
            return
            
        self.clicked = True
        button.disabled = True
        await interaction.response.edit_message(view=self)
        self.stop()

async def start_rob(cog, ctx, target: discord.Member):
    user_id = str(ctx.author.id)
    
    if target.id == ctx.author.id:
        await ctx.send("You want to rob yourself? Sonarr is judging you silently.")
        return
        
    if target.id == cog.bot.user.id:
        await ctx.send("You want to rob ME? Bold. And stupid.")
        try:
            await ctx.author.timeout(discord.utils.utcnow() + discord.utils.timedelta(minutes=1), reason="Tried to rob Sonarr")
        except:
            pass
        return
        
    # Check cooldown
    db.cursor.execute("SELECT last_rob FROM economy WHERE user_id = ?", (user_id,))
    row = db.cursor.fetchone()
    if row and row[0]:
        last_rob = row[0]
        now = time.time()
        if now - last_rob < 14400: # 4 hours
            rem = int((14400 - (now - last_rob)) / 60)
            await ctx.send(f"You're lying low. Try again in {rem} minutes.")
            return
            
    bal_target = get_balance(target.id)
    if bal_target["wallet"] < 200:
        await ctx.send("They're already suffering enough. Find a richer target.")
        return
        
    # Set cooldown immediately
    db.cursor.execute("UPDATE economy SET last_rob = ? WHERE user_id = ?", (time.time(), user_id))
    db.connection.commit()
    
    embed = discord.Embed(title=f"🦹 Robbery Attempt on {target.display_name}", color=0x95A5A6)
    embed.description = "Sneaking up..."
    
    msg = await ctx.send(embed=embed)
    
    delay = random.uniform(2.0, 5.0)
    await asyncio.sleep(delay)
    
    embed.description = (
        "Sneaking up...\n\n"
        "⚡ QUICK! Click the button! ⚡\n\n"
        "(You have 3 seconds!)"
    )
    
    view = RobView(ctx, target)
    await msg.edit(embed=embed, view=view)
    
    await view.wait()
    
    success_rate = 0.45 if view.clicked else 0.25
    
    if random.random() < success_rate:
        # Success
        bal = get_balance(target.id)
        # recheck balance just in case
        if bal["wallet"] < 200:
            await msg.edit(content="They hid their money! Robbery failed.", embed=None, view=None)
            return
            
        if bal["wallet"] >= 1000:
            pct = random.uniform(0.20, 0.25)
        else:
            pct = random.uniform(0.15, 0.20)
            
        stolen = int(bal["wallet"] * pct)
        
        update_wallet(target.id, -stolen)
        update_wallet(ctx.author.id, stolen)
        
        db.cursor.execute("UPDATE economy SET times_robbed = times_robbed + 1 WHERE user_id = ?", (str(target.id),))
        db.connection.commit()
        
        res_embed = discord.Embed(title="🦹 Robbery Successful", color=0x2ECC71)
        res_embed.description = f"You sneaked into {target.display_name}'s wallet and stole **{stolen:,} 🪙**.\nSlick."
        await msg.edit(embed=res_embed, view=None)
        
        try:
            await target.send(f"⚠️ You were robbed! Someone took **{stolen:,} 🪙** from your wallet.")
        except:
            pass
    else:
        # Failure
        robber_bal = get_balance(ctx.author.id)
        fine = min(150, robber_bal['wallet'])
        if fine > 0:
            update_wallet(ctx.author.id, -fine)
        res_embed = discord.Embed(title="🦹 Robbery Failed", color=0xE74C3C)
        if view.clicked:
            res_embed.description = f"CAUGHT. You tried, but they noticed. Pay the fine: **{fine} 🪙** for being bad at crime."
        else:
            res_embed.description = f"CAUGHT. You were too slow! Pay the fine: **{fine} 🪙** for being bad at crime."
        await msg.edit(embed=res_embed, view=None)
