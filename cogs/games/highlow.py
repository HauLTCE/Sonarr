import discord
import random
from utils.economy_helpers import update_wallet, record_gamble

HIGHLOW_MULTIPLIERS = {
    1: 1.4,
    2: 2.0,
    3: 3.2,
    4: 5.5,
    5: 10.0,
    6: 20.0,
    7: 50.0,
}

class HighLowView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.bet = bet
        self.round = 1
        self.current_number = random.randint(1, 100)
        
    def get_multiplier(self):
        return HIGHLOW_MULTIPLIERS.get(self.round, 50.0)

    def get_cashout_amount(self):
        # The cashout amount is based on the previously completed round.
        if self.round == 1:
            return 0
        return int(self.bet * HIGHLOW_MULTIPLIERS.get(self.round - 1, 50.0))

    def update_embed(self, embed: discord.Embed):
        # Calculate odds (99 possible other numbers since 1-100 range, excluding current)
        higher_chance = ((100 - self.current_number) / 99) * 100 if self.current_number < 100 else 0
        lower_chance = ((self.current_number - 1) / 99) * 100 if self.current_number > 1 else 0
        
        cashout_amount = self.get_cashout_amount()
        
        embed.title = f"📊 High Low           Bet: {self.bet:,} 🪙   Round {self.round}"
        if self.round > 1:
            embed.description = f"Current multiplier: {HIGHLOW_MULTIPLIERS.get(self.round-1, 50.0)}x ({cashout_amount:,} 🪙)\n\n"
        else:
            embed.description = ""
            
        embed.description += (
            f"Current number: **{self.current_number}**\n\n"
            f"📈 Higher: {higher_chance:.0f}% chance\n"
            f"📉 Lower:  {lower_chance:.0f}% chance"
        )
        
        # Update cashout button label
        for child in self.children:
            if child.custom_id == "cashout":
                if self.round == 1:
                    child.disabled = True
                    child.label = "💰 Cash Out"
                else:
                    child.disabled = False
                    child.label = f"💰 Cash Out: {cashout_amount:,}"

    async def end_game(self, interaction: discord.Interaction, won: bool, cashout: bool = False):
        for child in self.children:
            child.disabled = True
            
        embed = interaction.message.embeds[0]
        if cashout:
            winnings = self.get_cashout_amount()
            update_wallet(self.ctx.author.id, winnings)
            record_gamble(self.ctx.author.id, self.bet, winnings - self.bet)
            embed.description += f"\n\n✅ You cashed out {winnings:,} 🪙!"
            embed.color = 0x2ECC71
        elif won:
            # Reached max round or beyond logic
            pass
        else:
            record_gamble(self.ctx.author.id, self.bet, -self.bet)
            embed.description += f"\n\n❌ Wrong! You lost {self.bet:,} 🪙."
            embed.color = 0xE74C3C
            
        await interaction.response.edit_message(embed=embed, view=self)
        self.cog.active_games.discard(self.ctx.author.id)
        self.stop()

    @discord.ui.button(label="Higher", style=discord.ButtonStyle.primary, emoji="📈", custom_id="higher")
    async def higher(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        next_number = random.randint(1, 100)
        while next_number == self.current_number:
            next_number = random.randint(1, 100)
        
        if next_number > self.current_number:
            # Win
            self.current_number = next_number
            self.round += 1
            embed = interaction.message.embeds[0]
            if self.round > 7:
                await self.end_game(interaction, won=True, cashout=True)
            else:
                self.update_embed(embed)
                await interaction.response.edit_message(embed=embed, view=self)
        else:
            # Lose
            self.current_number = next_number
            await self.end_game(interaction, won=False)

    @discord.ui.button(label="Lower", style=discord.ButtonStyle.primary, emoji="📉", custom_id="lower")
    async def lower(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        next_number = random.randint(1, 100)
        while next_number == self.current_number:
            next_number = random.randint(1, 100)
        
        if next_number < self.current_number:
            # Win
            self.current_number = next_number
            self.round += 1
            embed = interaction.message.embeds[0]
            if self.round > 7:
                await self.end_game(interaction, won=True, cashout=True)
            else:
                self.update_embed(embed)
                await interaction.response.edit_message(embed=embed, view=self)
        else:
            # Lose
            self.current_number = next_number
            await self.end_game(interaction, won=False)

    @discord.ui.button(label="💰 Cash Out", style=discord.ButtonStyle.success, custom_id="cashout", disabled=True)
    async def cashout(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
        await self.end_game(interaction, won=True, cashout=True)

    async def on_timeout(self):
        self.cog.active_games.discard(self.ctx.author.id)
        if self.round > 1:
            # Auto cashout
            winnings = self.get_cashout_amount()
            update_wallet(self.ctx.author.id, winnings)
            record_gamble(self.ctx.author.id, self.bet, winnings - self.bet)

async def start_highlow(cog, ctx, bet):
    cog.active_games.add(ctx.author.id)
    try:
        update_wallet(ctx.author.id, -bet)
        view = HighLowView(cog, ctx, bet)
        embed = discord.Embed(color=0x3498DB)
        view.update_embed(embed)
        msg = await ctx.send(embed=embed, view=view)
        # Timeout will auto-cashout if applicable
    except Exception as e:
        update_wallet(ctx.author.id, bet)
        cog.active_games.discard(ctx.author.id)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
