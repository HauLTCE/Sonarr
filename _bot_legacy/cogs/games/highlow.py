import discord
import random
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

# House edge baked into every step. Because each round's multiplier is derived
# from the *actual* probability of the guess (see _step_multiplier), the
# expected value of any single round is (1 - HOUSE_EDGE) < 1 regardless of which
# side the player picks or what number is showing. That removes the old exploit
# where a fixed multiplier ladder + a visible number made "pick the obvious side"
# a guaranteed money printer.
HOUSE_EDGE = 0.05
MAX_ROUNDS = 7


class HighLowView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.bet = bet
        self.round = 1
        # Cumulative multiplier earned from rounds already won. Cashing out pays
        # bet * multiplier. Starts at 1.0 (nothing won yet → cashout disabled).
        self.multiplier = 1.0
        self.current_number = random.randint(1, 100)

    def _direction_odds(self):
        """Win probability for each direction.

        The next draw is uniform over 1..100 excluding the current number
        (99 outcomes), so P(higher) = (100 - n)/99 and P(lower) = (n - 1)/99.
        """
        higher = (100 - self.current_number) / 99 if self.current_number < 100 else 0.0
        lower = (self.current_number - 1) / 99 if self.current_number > 1 else 0.0
        return higher, lower

    def _step_multiplier(self, p_win: float) -> float:
        """Fair payout for a win of probability p_win, minus the house edge."""
        if p_win <= 0:
            return 0.0
        return (1 / p_win) * (1 - HOUSE_EDGE)

    def get_cashout_amount(self):
        # Payout reflects the rounds already banked; nothing to bank at round 1.
        if self.round == 1:
            return 0
        return int(self.bet * self.multiplier)

    def update_embed(self, embed: discord.Embed):
        higher_p, lower_p = self._direction_odds()
        higher_mult = self._step_multiplier(higher_p)
        lower_mult = self._step_multiplier(lower_p)

        cashout_amount = self.get_cashout_amount()

        embed.title = f"📊 High Low           Bet: {self.bet:,} 🪙   Round {self.round}"
        if self.round > 1:
            embed.description = f"Banked multiplier: {self.multiplier:.2f}x ({cashout_amount:,} 🪙)\n\n"
        else:
            embed.description = ""

        embed.description += (
            f"Current number: **{self.current_number}**\n\n"
            f"📈 Higher: {higher_p * 100:.0f}% → pays {higher_mult:.2f}x this round\n"
            f"📉 Lower:  {lower_p * 100:.0f}% → pays {lower_mult:.2f}x this round"
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
            # Unused: wins are routed through the cashout path at MAX_ROUNDS.
            pass
        else:
            record_gamble(self.ctx.author.id, self.bet, -self.bet)
            embed.description += f"\n\n❌ Wrong! You lost {self.bet:,} 🪙."
            embed.color = 0xE74C3C

        await interaction.response.edit_message(embed=embed, view=self)
        self.cog.active_games.discard(self.ctx.author.id)
        self.stop()

    async def _guess(self, interaction: discord.Interaction, direction: str):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return

        # Lock in the multiplier for THIS guess from the odds shown to the player.
        higher_p, lower_p = self._direction_odds()
        p_win = higher_p if direction == "higher" else lower_p
        step_mult = self._step_multiplier(p_win)

        next_number = random.randint(1, 100)
        while next_number == self.current_number:
            next_number = random.randint(1, 100)

        if direction == "higher":
            won = next_number > self.current_number
        else:
            won = next_number < self.current_number

        self.current_number = next_number

        if not won:
            await self.end_game(interaction, won=False)
            return

        # Win: bank the step multiplier and advance.
        self.multiplier = round(self.multiplier * step_mult, 2)
        self.round += 1
        if self.round > MAX_ROUNDS:
            await self.end_game(interaction, won=True, cashout=True)
        else:
            embed = interaction.message.embeds[0]
            self.update_embed(embed)
            await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Higher", style=discord.ButtonStyle.primary, emoji="📈", custom_id="higher")
    async def higher(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self._guess(interaction, "higher")

    @discord.ui.button(label="Lower", style=discord.ButtonStyle.primary, emoji="📉", custom_id="lower")
    async def lower(self, interaction: discord.Interaction, button: discord.ui.Button):
        await self._guess(interaction, "lower")

    @discord.ui.button(label="💰 Cash Out", style=discord.ButtonStyle.success, custom_id="cashout", disabled=True)
    async def cashout(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
        if self.round == 1:
            await interaction.response.send_message("Win at least one round before cashing out.", ephemeral=True)
            return
        await self.end_game(interaction, won=True, cashout=True)

    async def on_timeout(self):
        self.cog.active_games.discard(self.ctx.author.id)
        if self.round > 1:
            # Auto cashout the banked winnings.
            winnings = self.get_cashout_amount()
            update_wallet(self.ctx.author.id, winnings)
            record_gamble(self.ctx.author.id, self.bet, winnings - self.bet)


async def start_highlow(cog, ctx, bet):
    cog.active_games.add(ctx.author.id)

    # Atomic deduction: fail instead of clamping so we never front a bet the
    # player can't cover.
    if not spend_wallet(ctx.author.id, bet):
        cog.active_games.discard(ctx.author.id)
        await ctx.send("You don't have enough coins in your wallet for that bet.")
        return

    try:
        view = HighLowView(cog, ctx, bet)
        embed = discord.Embed(color=0x3498DB)
        view.update_embed(embed)
        await ctx.send(embed=embed, view=view)
        # Timeout will auto-cashout if applicable
    except Exception as e:
        update_wallet(ctx.author.id, bet)
        cog.active_games.discard(ctx.author.id)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
