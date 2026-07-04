import discord
from utils.economy_helpers import update_wallet, spend_wallet, record_gamble

class TicTacToeButton(discord.ui.Button):
    def __init__(self, x, y):
        super().__init__(style=discord.ButtonStyle.secondary, label="\u200b", row=y, custom_id=f"ttt_{x}_{y}")
        self.x = x
        self.y = y

    async def callback(self, interaction: discord.Interaction):
        view: TicTacToeView = self.view
        
        if interaction.user.id != view.current_turn.id:
            await interaction.response.send_message("It's not your turn!", ephemeral=True)
            return
            
        self.disabled = True
        
        if view.current_turn == view.p1:
            self.emoji = "❌"
            self.style = discord.ButtonStyle.danger
            view.board[self.y][self.x] = "X"
        else:
            self.emoji = "⭕"
            self.style = discord.ButtonStyle.success
            view.board[self.y][self.x] = "O"
            
        await view.check_winner(interaction)

class TicTacToeView(discord.ui.View):
    def __init__(self, cog, ctx, target, bet: int):
        super().__init__(timeout=60)
        self.cog = cog
        self.ctx = ctx
        self.target = target
        self.bet = bet
        
        self.p1 = ctx.author
        self.p2 = target
        self.current_turn = self.p1
        
        self.board = [
            ["", "", ""],
            ["", "", ""],
            ["", "", ""]
        ]
        
        for y in range(3):
            for x in range(3):
                self.add_item(TicTacToeButton(x, y))
                
    def check_line(self, l):
        if l[0] == l[1] == l[2] and l[0] != "":
            return l[0]
        return None
        
    async def check_winner(self, interaction: discord.Interaction):
        winner_mark = None
        
        # rows
        for row in self.board:
            w = self.check_line(row)
            if w: winner_mark = w
            
        # cols
        for col in range(3):
            w = self.check_line([self.board[0][col], self.board[1][col], self.board[2][col]])
            if w: winner_mark = w
            
        # diags
        w = self.check_line([self.board[0][0], self.board[1][1], self.board[2][2]])
        if w: winner_mark = w
        
        w = self.check_line([self.board[0][2], self.board[1][1], self.board[2][0]])
        if w: winner_mark = w
        
        is_tie = False
        if not winner_mark:
            is_tie = True
            for row in self.board:
                if "" in row:
                    is_tie = False
                    break
                    
        embed = interaction.message.embeds[0]
        
        if winner_mark or is_tie:
            for child in self.children:
                child.disabled = True
                
            if winner_mark:
                winner = self.p1 if winner_mark == "X" else self.p2
                loser = self.p2 if winner == self.p1 else self.p1
                
                embed.color = 0x2ECC71
                
                if self.bet > 0:
                    pot = self.bet * 2
                    tax = int(pot * 0.05)
                    payout = pot - tax
                    update_wallet(winner.id, payout)
                    record_gamble(winner.id, self.bet, payout - self.bet)
                    record_gamble(loser.id, self.bet, -self.bet)
                    embed.description = f"🏆 {winner.mention} wins **{payout:,} 🪙**!"
                else:
                    embed.description = f"🏆 {winner.mention} wins!"
            else:
                embed.color = 0xF1C40F
                if self.bet > 0:
                    update_wallet(self.p1.id, self.bet)
                    update_wallet(self.p2.id, self.bet)
                    embed.description = "🤝 It's a tie! Bets returned."
                else:
                    embed.description = "🤝 It's a tie!"
                    
            await interaction.response.edit_message(embed=embed, view=self)
            self.cog.active_games.discard(self.p1.id)
            self.cog.active_games.discard(self.p2.id)
            self.stop()
        else:
            self.current_turn = self.p2 if self.current_turn == self.p1 else self.p1
            
            if self.bet > 0:
                embed.description = f"Bet: {self.bet:,} 🪙\n\n{self.p1.mention} (❌) vs {self.p2.mention} (⭕)\n{self.current_turn.mention}'s turn"
            else:
                embed.description = f"{self.p1.mention} (❌) vs {self.p2.mention} (⭕)\n{self.current_turn.mention}'s turn"
                
            await interaction.response.edit_message(embed=embed, view=self)
            self.timeout = 60.0

    async def on_timeout(self):
        for child in self.children:
            child.disabled = True
            
        if self.bet > 0:
            update_wallet(self.p1.id, self.bet)
            update_wallet(self.p2.id, self.bet)
            
        self.cog.active_games.discard(self.p1.id)
        self.cog.active_games.discard(self.p2.id)

class TTTAcceptView(discord.ui.View):
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

async def start_ttt(cog, ctx, target, bet: int = 0):
    cog.active_games.add(ctx.author.id)
    cog.active_games.add(target.id)

    def _release():
        cog.active_games.discard(ctx.author.id)
        cog.active_games.discard(target.id)

    try:
        # M2: defer charging until the challenge is accepted (no locked coins
        # during the pending window).
        embed = discord.Embed(title="❌⭕ Tic-Tac-Toe", color=0x3498DB)
        if bet > 0:
            embed.description = f"{ctx.author.mention} challenges {target.mention}!\nBet: {bet:,} 🪙\n\n{target.mention} has 60 seconds to respond."
        else:
            embed.description = f"{ctx.author.mention} challenges {target.mention} to a friendly game!\n\n{target.mention} has 60 seconds to respond."
            
        accept_view = TTTAcceptView(target)
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
            game_view = TicTacToeView(cog, ctx, target, bet)
            
            if bet > 0:
                embed.description = f"Bet: {bet:,} 🪙\n\n{ctx.author.mention} (❌) vs {target.mention} (⭕)\n{ctx.author.mention}'s turn"
            else:
                embed.description = f"{ctx.author.mention} (❌) vs {target.mention} (⭕)\n{ctx.author.mention}'s turn"
                
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
