import discord
import random
import logging

logger = logging.getLogger("bot")


class TicTacToeButton(discord.ui.Button):
    def __init__(self, x: int, y: int):
        super().__init__(style=discord.ButtonStyle.secondary, label="\u200b", row=y)
        self.x = x
        self.y = y

    async def callback(self, interaction: discord.Interaction):
        view: TicTacToeView = self.view
        state = view.board[self.y][self.x]
        if state in (view.X, view.O):
            return

        if view.current_player != interaction.user:
            await interaction.response.send_message("It's not your turn!", ephemeral=True)
            return

        if view.turn == view.X:
            self.style = discord.ButtonStyle.danger
            self.label = 'X'
            self.disabled = True
            view.board[self.y][self.x] = view.X
            view.turn = view.O
            content = f"It is now {view.player_o.mention}'s turn"
        else:
            self.style = discord.ButtonStyle.success
            self.label = 'O'
            self.disabled = True
            view.board[self.y][self.x] = view.O
            view.turn = view.X
            content = f"It is now {view.player_x.mention}'s turn"

        winner = view.check_winner()
        if winner is not None:
            if winner == view.X:
                content = f"🏆 {view.player_x.mention} won!"
            elif winner == view.O:
                content = f"🏆 {view.player_o.mention} won!"
            else:
                content = "It's a tie!"

            for child in view.children:
                child.disabled = True

            view.stop()

        await interaction.response.edit_message(content=content, view=view)


class TicTacToeView(discord.ui.View):
    X = -1
    O = 1

    def __init__(self, player_x, player_o):
        super().__init__()
        self.player_x = player_x
        self.player_o = player_o
        self.turn = self.X
        self.board = [[0, 0, 0], [0, 0, 0], [0, 0, 0]]
        for x in range(3):
            for y in range(3):
                self.add_item(TicTacToeButton(x, y))

    @property
    def current_player(self):
        return self.player_x if self.turn == self.X else self.player_o

    def check_winner(self):
        for row in self.board:
            value = sum(row)
            if value == 3: return self.O
            if value == -3: return self.X
        for col in range(3):
            value = self.board[0][col] + self.board[1][col] + self.board[2][col]
            if value == 3: return self.O
            if value == -3: return self.X
        d1 = self.board[0][0] + self.board[1][1] + self.board[2][2]
        d2 = self.board[0][2] + self.board[1][1] + self.board[2][0]
        if d1 == 3 or d2 == 3: return self.O
        if d1 == -3 or d2 == -3: return self.X
        if all(i != 0 for row in self.board for i in row):
            return 0
        return None


class BlackjackView(discord.ui.View):
    def __init__(self, game_cog, ctx, bet):
        super().__init__(timeout=60)
        self.game_cog = game_cog
        self.ctx = ctx
        self.bet = bet
        self.deck = [2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 10, 10, 11] * 4
        random.shuffle(self.deck)
        self.player_hand = [self.draw(), self.draw()]
        self.dealer_hand = [self.draw(), self.draw()]
        self.game_over = False

    def draw(self):
        return self.deck.pop()

    def calculate_score(self, hand):
        score = sum(hand)
        num_aces = hand.count(11)
        while score > 21 and num_aces:
            score -= 10
            num_aces -= 1
        return score

    async def update_message(self, interaction, result_msg=None):
        p_score = self.calculate_score(self.player_hand)
        d_score = self.calculate_score(self.dealer_hand)

        desc = f"**Your Hand:** {self.player_hand} (Score: **{p_score}**)\n"
        
        if self.game_over:
            desc += f"**Dealer Hand:** {self.dealer_hand} (Score: **{d_score}**)\n\n"
            desc += result_msg
            color = 0x00ff00 if "Won" in result_msg else 0xff0000
        else:
            desc += f"**Dealer Hand:** [{self.dealer_hand[0]}, ?]\n"
            color = 0x3498db

        embed = discord.Embed(title="🃏 Blackjack", description=desc, color=color)
        embed.set_footer(text=f"Bet: ${self.bet}")
        
        if self.game_over:
            self.clear_items()
            
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Hit", style=discord.ButtonStyle.success)
    async def hit(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user != self.ctx.author: return
        self.player_hand.append(self.draw())
        score = self.calculate_score(self.player_hand)
        if score > 21:
            self.game_over = True
            self.game_cog.update_balance(self.ctx.author.id, -self.bet)
            await self.update_message(interaction, f"💥 **Bust!** You went over 21. You lost **${self.bet}**.")
            logger.info(f"User {self.ctx.author.display_name} busted in blackjack and lost ${self.bet}")
        else:
            await self.update_message(interaction)

    @discord.ui.button(label="Stand", style=discord.ButtonStyle.danger)
    async def stand(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user != self.ctx.author: return
        self.game_over = True
        while self.calculate_score(self.dealer_hand) < 17:
            self.dealer_hand.append(self.draw())
        p_score = self.calculate_score(self.player_hand)
        d_score = self.calculate_score(self.dealer_hand)
        
        if d_score > 21:
            self.game_cog.update_balance(self.ctx.author.id, self.bet)
            msg = f"🎉 **Dealer Busted!** You won **${self.bet}**!"
            logger.info(f"User {self.ctx.author.display_name} won ${self.bet} on blackjack (dealer busted)")
        elif p_score > d_score:
            self.game_cog.update_balance(self.ctx.author.id, self.bet)
            msg = f"🎉 **You Won!** {p_score} vs {d_score}."
            logger.info(f"User {self.ctx.author.display_name} won ${self.bet} on blackjack ({p_score} vs {d_score})")
        elif p_score == d_score:
            msg = "🤝 **Push!** It's a tie. Money returned."
            logger.info(f"User {self.ctx.author.display_name} pushed on blackjack ({p_score} vs {d_score})")
        else:
            self.game_cog.update_balance(self.ctx.author.id, -self.bet)
            msg = f"💸 **Dealer Won.** {d_score} vs {p_score}."
            logger.info(f"User {self.ctx.author.display_name} lost ${self.bet} on blackjack ({d_score} vs {p_score})")
        await self.update_message(interaction, msg)


class HighLowView(discord.ui.View):
    def __init__(self, game_cog, ctx, bet, number):
        super().__init__(timeout=60)
        self.game_cog = game_cog
        self.ctx = ctx
        self.bet = bet
        self.number = number

    async def end_game(self, interaction, choice):
        next_num = random.randint(1, 100)
        while next_num == self.number:
            next_num = random.randint(1, 100)

        won = False
        if choice == "higher" and next_num > self.number: won = True
        elif choice == "lower" and next_num < self.number: won = True

        if won:
            self.game_cog.update_balance(self.ctx.author.id, self.bet)
            msg = f"🎉 Correct! The number was **{next_num}**. You won **${self.bet}**!"
            color = 0x00ff00
            logger.info(f"User {self.ctx.author.display_name} won ${self.bet} on highlow (number {next_num})")
        else:
            self.game_cog.update_balance(self.ctx.author.id, -self.bet)
            msg = f"❌ Wrong! The number was **{next_num}**. You lost **${self.bet}**."
            color = 0xff0000
            logger.info(f"User {self.ctx.author.display_name} lost ${self.bet} on highlow (number {next_num})")

        embed = discord.Embed(title="📉 High Low", description=msg, color=color)
        self.clear_items()
        await interaction.response.edit_message(embed=embed, view=self)

    @discord.ui.button(label="Lower", style=discord.ButtonStyle.primary)
    async def lower(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user != self.ctx.author: return
        await self.end_game(interaction, "lower")

    @discord.ui.button(label="Higher", style=discord.ButtonStyle.primary)
    async def higher(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user != self.ctx.author: return
        await self.end_game(interaction, "higher")


class DuelView(discord.ui.View):
    def __init__(self, game_cog, ctx, opponent, bet):
        super().__init__(timeout=120)
        self.game_cog = game_cog
        self.ctx = ctx
        self.players = [ctx.author, opponent]
        self.turn_idx = 0
        self.bet = bet
        self.chamber = random.randint(0, 5)
        self.current_shot = 0
        self.resolved = False

    async def _end_duel(self, interaction, loser, winner):
        # Mark resolved to avoid double execution
        if self.resolved:
            return
        self.resolved = True

        # Winner receives the whole pot (both bets were deducted at start)
        pot = self.bet * 2
        self.game_cog.update_balance(winner.id, pot,)
        logger.info(f"Duel result: {winner.display_name} won ${pot} vs {loser.display_name}")

        embed = discord.Embed(title="💀 Duel Ended", description=f"**BANG!** {loser.mention} is dead.\n{winner.mention} wins **${pot}**!", color=0xff0000)

        # Disable buttons and update message
        for child in self.children:
            child.disabled = True

        try:
            await interaction.response.edit_message(embed=embed, view=self)
        except Exception:
            # Fallback: try editing channel message
            try:
                msg = interaction.message
                await msg.edit(embed=embed, view=self)
            except Exception:
                pass
        finally:
            self.stop()

    @discord.ui.button(label="🔥 Pull Trigger", style=discord.ButtonStyle.danger)
    async def trigger(self, interaction: discord.Interaction, button: discord.ui.Button):
        current_shooter = self.players[self.turn_idx]
        
        if interaction.user != current_shooter:
            await interaction.response.send_message("Not your turn!", ephemeral=True)
            return

        # Prevent re-entrancy if duel already resolved
        if self.resolved:
            await interaction.response.send_message("This duel has already finished.", ephemeral=True)
            return

        if self.current_shot == self.chamber:
            loser = current_shooter
            winner = self.players[1] if self.turn_idx == 0 else self.players[0]
            await self._end_duel(interaction, loser, winner)
        else:
            self.current_shot += 1
            self.turn_idx = 1 if self.turn_idx == 0 else 0
            next_shooter = self.players[self.turn_idx]
            # Update content to show who's turn it is
            try:
                await interaction.response.edit_message(content=f"💨 Click... empty. {next_shooter.mention}'s turn.", view=self)
            except Exception:
                pass

    async def on_timeout(self):
        # If the duel times out, refund both players if not resolved
        if not self.resolved:
            for p in self.players:
                self.game_cog.update_balance(p.id, self.bet)
            logger.info(f"Duel timed out. Refunded ${self.bet} to each player: {', '.join(p.display_name for p in self.players)}")
            channel = self.ctx.channel
            try:
                await channel.send("⏳ Duel timed out. Bets refunded.")
            except Exception:
                pass
        self.resolved = True
        # ensure view is stopped
        try:
            self.stop()
        except Exception:
            pass
