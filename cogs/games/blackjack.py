import discord
import random
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

CARD_FACES = {
    2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8',
    9: '9', 10: '10', 11: 'J', 12: 'Q', 13: 'K', 14: 'A'
}
SUITS = ['♠', '♥', '♦', '♣']

def format_card(card):
    value, suit_idx = card
    display_value = CARD_FACES[value]
    return f"[{display_value}{SUITS[suit_idx]}]"

def calculate_score(hand):
    score = 0
    aces = 0
    for value, _ in hand:
        if value == 14:
            aces += 1
            score += 11
        elif value >= 10:
            score += 10
        else:
            score += value
            
    while score > 21 and aces > 0:
        score -= 10
        aces -= 1
        
    return score

class BlackjackView(discord.ui.View):
    def __init__(self, cog, ctx, bet: int):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.bet = bet
        self.deck = [(v, s) for v in range(2, 15) for s in range(4)]
        random.shuffle(self.deck)
        
        self.player_hand = [self.deck.pop(), self.deck.pop()]
        self.dealer_hand = [self.deck.pop(), self.deck.pop()]
        
        self.doubled = False
        # Idempotency guard so the wager is settled exactly once, whether the
        # game ends via a button or via on_timeout (prevents double payout if a
        # click races the timeout).
        self.resolved = False
        # Message the view is attached to, so on_timeout can update it.
        self.message = None
        
    def generate_embed(self, dealer_hidden=True):
        embed = discord.Embed(title="🃏 Blackjack", color=0x2ECC71)
        
        player_score = calculate_score(self.player_hand)
        player_cards = " ".join([format_card(c) for c in self.player_hand])
        
        if dealer_hidden:
            dealer_cards = f"{format_card(self.dealer_hand[0])} [??]"
            dealer_score = "?"
        else:
            dealer_cards = " ".join([format_card(c) for c in self.dealer_hand])
            dealer_score = calculate_score(self.dealer_hand)
            
        embed.description = (
            f"Bet: {self.bet * 2 if self.doubled else self.bet:,} 🪙\n\n"
            f"**Your Hand:** {player_cards}\n"
            f"Score: {player_score}\n\n"
            f"**Dealer:** {dealer_cards}\n"
            f"Score: {dealer_score}"
        )
        return embed

    def _settle(self):
        """Play out the dealer and settle the wager exactly once.

        Returns the final embed, or None if the wager was already settled.
        Kept free of any interaction so both end_game (button) and on_timeout
        (auto-stand) can share it.
        """
        if self.resolved:
            return None
        self.resolved = True

        player_score = calculate_score(self.player_hand)
        dealer_score = calculate_score(self.dealer_hand)

        while dealer_score < 17 and player_score <= 21:
            self.dealer_hand.append(self.deck.pop())
            dealer_score = calculate_score(self.dealer_hand)

        embed = self.generate_embed(dealer_hidden=False)

        final_bet = self.bet * 2 if self.doubled else self.bet

        if player_score > 21:
            record_gamble(self.ctx.author.id, final_bet, -final_bet)
            embed.color = 0xE74C3C
            embed.description += f"\n\n💥 You busted! You lost {final_bet:,} 🪙."
        elif dealer_score > 21:
            winnings = final_bet * 2
            update_wallet(self.ctx.author.id, winnings)
            record_gamble(self.ctx.author.id, final_bet, winnings - final_bet)
            embed.color = 0x2ECC71
            embed.description += f"\n\n✅ Dealer busted! You won {winnings:,} 🪙."
        elif player_score > dealer_score:
            winnings = final_bet * 2
            update_wallet(self.ctx.author.id, winnings)
            record_gamble(self.ctx.author.id, final_bet, winnings - final_bet)
            embed.color = 0x2ECC71
            embed.description += f"\n\n✅ You won! (+{winnings:,} 🪙)"
        elif player_score < dealer_score:
            record_gamble(self.ctx.author.id, final_bet, -final_bet)
            embed.color = 0xE74C3C
            embed.description += f"\n\n❌ Dealer wins. You lost {final_bet:,} 🪙."
        else:
            update_wallet(self.ctx.author.id, final_bet)
            embed.color = 0xF1C40F
            embed.description += f"\n\n🤝 Push. Your bet of {final_bet:,} 🪙 was returned."

        return embed

    async def end_game(self, interaction: discord.Interaction):
        for child in self.children:
            child.disabled = True

        embed = self._settle()
        if embed is None:
            # Already settled (e.g. by a timeout that just fired) — just refresh.
            await interaction.response.edit_message(view=self)
            return

        await interaction.response.edit_message(embed=embed, view=self)
        self.cog.active_games.discard(self.ctx.author.id)
        self.stop()

    @discord.ui.button(label="Hit", style=discord.ButtonStyle.primary, emoji="🟢", custom_id="hit")
    async def hit(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        for child in self.children:
            if child.custom_id == "double":
                child.disabled = True
                
        self.player_hand.append(self.deck.pop())
        
        if calculate_score(self.player_hand) > 21:
            await self.end_game(interaction)
        else:
            embed = self.generate_embed(dealer_hidden=True)
            await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Stand", style=discord.ButtonStyle.danger, emoji="🔴", custom_id="stand")
    async def stand(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        await self.end_game(interaction)

    @discord.ui.button(label="Double", style=discord.ButtonStyle.secondary, emoji="🟡", custom_id="double")
    async def double(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user.id != self.ctx.author.id:
            await interaction.response.send_message("Not your game.", ephemeral=True)
            return
            
        # Atomic: only take the second stake if the wallet can actually cover it.
        if not spend_wallet(self.ctx.author.id, self.bet):
            await interaction.response.send_message("You don't have enough coins to double down.", ephemeral=True)
            return

        self.doubled = True
        
        self.player_hand.append(self.deck.pop())
        await self.end_game(interaction)

    async def on_timeout(self):
        # Auto-stand instead of refunding. Walking away from a bad hand must not
        # be a risk-free escape — resolve the hand exactly as a Stand would.
        for child in self.children:
            child.disabled = True

        embed = self._settle()
        self.cog.active_games.discard(self.ctx.author.id)
        if embed is None:
            return
        if self.message is not None:
            try:
                await self.message.edit(embed=embed, view=self)
            except Exception:
                pass

async def start_blackjack(cog, ctx, bet: int):
    cog.active_games.add(ctx.author.id)

    # Atomic deduction: fail instead of clamping to 0 so we never deal a hand the
    # player can't actually pay for.
    if not spend_wallet(ctx.author.id, bet):
        cog.active_games.discard(ctx.author.id)
        await ctx.send("You don't have enough coins in your wallet for that bet.")
        return

    try:
        view = BlackjackView(cog, ctx, bet)
        
        # Check natural blackjack
        player_score = calculate_score(view.player_hand)
        dealer_score = calculate_score(view.dealer_hand)
        
        if player_score == 21:
            # Resolves immediately without the interactive view. Mark it settled
            # and stop it so a stray timeout can never re-pay this hand.
            view.resolved = True
            view.stop()
            embed = view.generate_embed(dealer_hidden=False)
            if dealer_score == 21:
                update_wallet(ctx.author.id, bet)
                embed.description += f"\n\n🤝 Both have Blackjack! Push. Bet returned."
                embed.color = 0xF1C40F
            else:
                winnings = int(bet + bet * 1.5)
                update_wallet(ctx.author.id, winnings)
                record_gamble(ctx.author.id, bet, winnings - bet)
                embed.description += f"\n\n🎉 Blackjack! You won {winnings:,} 🪙!"
                embed.color = 0x2ECC71
                
            await ctx.send(embed=embed)
            cog.active_games.discard(ctx.author.id)
            return
            
        embed = view.generate_embed(dealer_hidden=True)
        view.message = await ctx.send(embed=embed, view=view)
        
    except Exception as e:
        update_wallet(ctx.author.id, bet)
        cog.active_games.discard(ctx.author.id)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
