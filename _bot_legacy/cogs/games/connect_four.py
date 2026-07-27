import discord
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

class ConnectFourButton(discord.ui.Button):
    def __init__(self, col, row_idx):
        super().__init__(style=discord.ButtonStyle.primary, label=str(col+1), row=row_idx, custom_id=f"c4_{col}")
        self.col = col

    async def callback(self, interaction: discord.Interaction):
        view: ConnectFourView = self.view
        
        if interaction.user.id != view.current_turn.id:
            await interaction.response.send_message("It's not your turn!", ephemeral=True)
            return
            
        success = view.drop_piece(self.col)
        if not success:
            await interaction.response.send_message("That column is full!", ephemeral=True)
            return
            
        await view.check_winner(interaction)

class ConnectFourView(discord.ui.View):
    def __init__(self, cog, ctx, target, bet: int):
        super().__init__(timeout=120)
        self.cog = cog
        self.ctx = ctx
        self.target = target
        self.bet = bet
        
        self.p1 = ctx.author
        self.p2 = target
        self.current_turn = self.p1
        
        self.board = [["" for _ in range(7)] for _ in range(6)]
        
        for i in range(4):
            self.add_item(ConnectFourButton(i, 0))
        for i in range(4, 7):
            self.add_item(ConnectFourButton(i, 1))
            
    def drop_piece(self, col):
        mark = "🔴" if self.current_turn == self.p1 else "🟡"
        for row in reversed(range(6)):
            if self.board[row][col] == "":
                self.board[row][col] = mark
                return True
        return False
        
    def check_win(self, mark):
        # horizontal
        for row in range(6):
            for col in range(4):
                if all(self.board[row][col+i] == mark for i in range(4)):
                    return True
        # vertical
        for col in range(7):
            for row in range(3):
                if all(self.board[row+i][col] == mark for i in range(4)):
                    return True
        # diagonal \
        for row in range(3):
            for col in range(4):
                if all(self.board[row+i][col+i] == mark for i in range(4)):
                    return True
        # diagonal /
        for row in range(3, 6):
            for col in range(4):
                if all(self.board[row-i][col+i] == mark for i in range(4)):
                    return True
        return False
        
    def check_tie(self):
        return all(self.board[0][col] != "" for col in range(7))
        
    def format_board(self):
        text = ""
        for row in range(6):
            for col in range(7):
                text += self.board[row][col] or "⬜"
            text += "\n"
        return text

    async def check_winner(self, interaction: discord.Interaction):
        mark = "🔴" if self.current_turn == self.p1 else "🟡"
        is_win = self.check_win(mark)
        is_tie = False
        
        if not is_win:
            is_tie = self.check_tie()
            
        embed = interaction.message.embeds[0]
        
        if is_win or is_tie:
            for child in self.children:
                child.disabled = True
                
            if is_win:
                winner = self.current_turn
                loser = self.p2 if winner == self.p1 else self.p1
                
                embed.color = 0x2ECC71
                
                if self.bet > 0:
                    pot = self.bet * 2
                    tax = int(pot * 0.05)
                    payout = pot - tax
                    update_wallet(winner.id, payout)
                    record_gamble(winner.id, self.bet, payout - self.bet)
                    record_gamble(loser.id, self.bet, -self.bet)
                    embed.description = f"🏆 {winner.mention} wins **{payout:,} 🪙**!\n\n{self.format_board()}"
                else:
                    embed.description = f"🏆 {winner.mention} wins!\n\n{self.format_board()}"
            else:
                embed.color = 0xF1C40F
                if self.bet > 0:
                    update_wallet(self.p1.id, self.bet)
                    update_wallet(self.p2.id, self.bet)
                    embed.description = f"🤝 It's a tie! Bets returned.\n\n{self.format_board()}"
                else:
                    embed.description = f"🤝 It's a tie!\n\n{self.format_board()}"
                    
            await interaction.response.edit_message(embed=embed, view=self)
            self.cog.active_games.discard(self.p1.id)
            self.cog.active_games.discard(self.p2.id)
            self.stop()
        else:
            self.current_turn = self.p2 if self.current_turn == self.p1 else self.p1
            
            if self.bet > 0:
                embed.description = f"Bet: {self.bet:,} 🪙\n\n{self.p1.mention} (🔴) vs {self.p2.mention} (🟡)\n{self.current_turn.mention}'s turn\n\n{self.format_board()}"
            else:
                embed.description = f"{self.p1.mention} (🔴) vs {self.p2.mention} (🟡)\n{self.current_turn.mention}'s turn\n\n{self.format_board()}"
                
            await interaction.response.edit_message(embed=embed, view=self)
            self.timeout = 120.0

    async def on_timeout(self):
        for child in self.children:
            child.disabled = True
            
        if self.bet > 0:
            update_wallet(self.p1.id, self.bet)
            update_wallet(self.p2.id, self.bet)
            
        self.cog.active_games.discard(self.p1.id)
        self.cog.active_games.discard(self.p2.id)

class C4AcceptView(discord.ui.View):
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

async def start_c4(cog, ctx, target, bet: int = 0):
    cog.active_games.add(ctx.author.id)
    cog.active_games.add(target.id)

    def _release():
        cog.active_games.discard(ctx.author.id)
        cog.active_games.discard(target.id)

    try:
        # M2: defer charging until the challenge is accepted.
        embed = discord.Embed(title="🔴🟡 Connect Four", color=0x3498DB)
        if bet > 0:
            embed.description = f"{ctx.author.mention} challenges {target.mention}!\nBet: {bet:,} 🪙\n\n{target.mention} has 60 seconds to respond."
        else:
            embed.description = f"{ctx.author.mention} challenges {target.mention} to a friendly game!\n\n{target.mention} has 60 seconds to respond."
            
        accept_view = C4AcceptView(target)
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

        # Consent given — charge both atomically (M1).
        if bet > 0:
            if not spend_wallet(ctx.author.id, bet):
                await msg.edit(content="You no longer have enough coins. Game cancelled.", embed=None)
                _release()
                return
            if not spend_wallet(target.id, bet):
                update_wallet(ctx.author.id, bet)
                await msg.edit(content=f"{target.display_name} no longer has enough coins. Game cancelled.", embed=None)
                _release()
                return
            
        # Start game (view owns active_games / payouts from here).
        try:
            game_view = ConnectFourView(cog, ctx, target, bet)
            
            if bet > 0:
                embed.description = f"Bet: {bet:,} 🪙\n\n{ctx.author.mention} (🔴) vs {target.mention} (🟡)\n{ctx.author.mention}'s turn\n\n{game_view.format_board()}"
            else:
                embed.description = f"{ctx.author.mention} (🔴) vs {target.mention} (🟡)\n{ctx.author.mention}'s turn\n\n{game_view.format_board()}"
                
            await msg.edit(embed=embed, view=game_view)
        except Exception:
            if bet > 0:
                update_wallet(ctx.author.id, bet)
                update_wallet(target.id, bet)
            _release()
            raise
        
    except Exception as e:
        _release()
        await ctx.send("An error occurred.")
        raise e
