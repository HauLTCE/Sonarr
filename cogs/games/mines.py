import discord
import random
from utils.economy_helpers import update_wallet, record_gamble

def calculate_mines_multiplier(total_tiles, mines, revealed):
    safe_remaining = total_tiles - mines - revealed
    tiles_remaining = total_tiles - revealed
    
    if tiles_remaining <= 0 or safe_remaining <= 0:
        return 0
        
    p_safe = safe_remaining / tiles_remaining
    house_edge = 0.03
    fair_multiplier = (1 / p_safe) * (1 - house_edge)
    
    return round(fair_multiplier, 2)

class MineButton(discord.ui.Button):
    def __init__(self, x, y, is_mine):
        super().__init__(style=discord.ButtonStyle.secondary, label="\u200b", emoji="⬜", row=y, custom_id=f"mine_{x}_{y}")
        self.is_mine = is_mine
        self.revealed = False

    async def callback(self, interaction: discord.Interaction):
        view: MinesView = self.view
        
        if interaction.user.id != view.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        if self.revealed:
            return
            
        self.revealed = True
        self.disabled = True
        
        if self.is_mine:
            self.emoji = "💣"
            self.style = discord.ButtonStyle.danger
            await view.hit_mine(interaction)
        else:
            self.emoji = "💎"
            self.style = discord.ButtonStyle.success
            await view.reveal_safe(interaction)

class MinesView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int, mines: int):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.bet = bet
        self.mines = mines
        self.revealed_count = 0
        self.multiplier = 1.00
        self.total_tiles = 25
        
        grid = [(x, y) for x in range(5) for y in range(5)]
        mine_locations = set(random.sample(grid, mines))
        
        self.mine_buttons = []
        
        for y in range(5):
            for x in range(5):
                is_mine = (x, y) in mine_locations
                btn = MineButton(x, y, is_mine)
                self.add_item(btn)
                self.mine_buttons.append(btn)
                
        self.cashout_btn = discord.ui.Button(label="💰 Cash Out", style=discord.ButtonStyle.primary, row=4) # Custom row not strictly needed for non-grid but we can append it or use a separate view, wait discord allows max 5 items per row, 5 rows max = 25 buttons. So we CANNOT add a 26th button to this view!
        # Ah! A 5x5 grid takes all 25 slots. Where does the Cashout button go?
        # Maybe we should use a 5x4 grid = 20 buttons, or we just instruct them to type `cashout`?
        # Or we can put cashout as one of the buttons, e.g. 5x5 grid but one button is Cash Out. 
        # No, 02_casino_games.md says: 5x5 grid (25 tiles).
        # We can just check messages for "cashout".
        
    async def hit_mine(self, interaction: discord.Interaction):
        for btn in self.mine_buttons:
            btn.disabled = True
            if btn.is_mine and not btn.revealed:
                btn.emoji = "💣"
                btn.style = discord.ButtonStyle.danger
                
        record_gamble(self.ctx.author.id, self.bet, -self.bet)
        
        embed = interaction.message.embeds[0]
        embed.color = 0xE74C3C
        embed.title = f"💣 Mines — GAME OVER"
        embed.description = (
            f"Bet: {self.bet:,} 🪙 | Mines: {self.mines}\n\n"
            f"💥 **BOOM!** You hit a mine.\n"
            f"You lost {self.bet:,} 🪙."
        )
        await interaction.response.edit_message(embed=embed, view=self)
        self.cog.active_games.discard(self.ctx.author.id)
        self.stop()
        
    async def reveal_safe(self, interaction: discord.Interaction):
        self.revealed_count += 1
        
        # Calculate new multiplier
        # The multiplier is cumulative? No, calculate_mines_multiplier returns the fair multiplier for revealing the next tile... wait, 02_casino_games.md says "Cumulative Multiplier"
        # It says "The multiplier at each step is calculated... fair_multiplier = (1 / p_safe) * (1 - house_edge)".
        # Actually, the example table shows:
        # 1 tile = 1.10x, 2 tiles = 1.22x...
        # So it's cumulative. We multiply self.multiplier by calculate_mines_multiplier(25, mines, revealed)
        
        next_mult = calculate_mines_multiplier(self.total_tiles, self.mines, self.revealed_count - 1)
        if self.revealed_count == 1:
            self.multiplier = next_mult
        else:
            self.multiplier *= next_mult
            
        self.multiplier = round(self.multiplier, 2)
        
        cashout_amount = int(self.bet * self.multiplier)
        
        embed = interaction.message.embeds[0]
        
        if self.revealed_count == self.total_tiles - self.mines:
            # Cleared all safes
            for btn in self.mine_buttons:
                btn.disabled = True
                
            update_wallet(self.ctx.author.id, cashout_amount)
            record_gamble(self.ctx.author.id, self.bet, cashout_amount - self.bet)
            
            embed.color = 0x2ECC71
            embed.title = f"💣 Mines — CLEARED!"
            embed.description = (
                f"Bet: {self.bet:,} 🪙 | Mines: {self.mines}\n\n"
                f"🎉 **JACKPOT!** All safe tiles cleared!\n"
                f"Multiplier: {self.multiplier}x\n"
                f"Winnings: {cashout_amount:,} 🪙!"
            )
            await interaction.response.edit_message(embed=embed, view=self)
            self.cog.active_games.discard(self.ctx.author.id)
            self.stop()
        else:
            embed.description = (
                f"Bet: {self.bet:,} 🪙 | Mines: {self.mines}\n"
                f"Current: {self.multiplier}x ({cashout_amount:,} 🪙)\n\n"
                f"Type `cashout` in chat to secure your winnings."
            )
            await interaction.response.edit_message(embed=embed, view=self)

    async def cashout_game(self, message: discord.Message):
        if self.revealed_count == 0:
            return
            
        for btn in self.mine_buttons:
            btn.disabled = True
            if btn.is_mine:
                btn.emoji = "💣"
                
        cashout_amount = int(self.bet * self.multiplier)
        update_wallet(self.ctx.author.id, cashout_amount)
        record_gamble(self.ctx.author.id, self.bet, cashout_amount - self.bet)
        
        embed = discord.Embed(title="💣 Mines — CASHED OUT", color=0x2ECC71)
        embed.description = (
            f"Bet: {self.bet:,} 🪙 | Mines: {self.mines}\n\n"
            f"✅ You cashed out at {self.multiplier}x.\n"
            f"Winnings: {cashout_amount:,} 🪙!"
        )
        
        await message.channel.send(embed=embed, view=self)
        self.cog.active_games.discard(self.ctx.author.id)
        self.stop()

    async def on_timeout(self):
        self.cog.active_games.discard(self.ctx.author.id)
        # Auto cashout if any revealed
        if self.revealed_count > 0:
            cashout_amount = int(self.bet * self.multiplier)
            update_wallet(self.ctx.author.id, cashout_amount)
            record_gamble(self.ctx.author.id, self.bet, cashout_amount - self.bet)

async def start_mines(cog, ctx, bet: int, mines: int):
    cog.active_games.add(ctx.author.id)
    try:
        update_wallet(ctx.author.id, -bet)
        
        view = MinesView(cog, ctx, bet, mines)
        
        embed = discord.Embed(title="💣 Mines", color=0x3498DB)
        embed.description = (
            f"Bet: {bet:,} 🪙 | Mines: {mines}\n"
            f"Current: 1.00x\n\n"
            f"Type `cashout` in chat at any time to secure your winnings."
        )
        
        msg = await ctx.send(embed=embed, view=view)
        
        def check(m):
            return m.author == ctx.author and m.channel == ctx.channel and m.content.lower() == 'cashout'
            
        while not view.is_finished():
            try:
                # Wait for cashout message
                m = await cog.bot.wait_for('message', check=check, timeout=120.0)
                if not view.is_finished():
                    await view.cashout_game(m)
                    try:
                        await msg.edit(view=view)
                    except:
                        pass
            except discord.TimeoutError:
                break
                
    except Exception as e:
        update_wallet(ctx.author.id, bet)
        cog.active_games.discard(ctx.author.id)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
