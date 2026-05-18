import discord
import random
import asyncio
from utils.economy_helpers import update_wallet

WORD_STARTS = ["APPLE", "BANANA", "CAT", "DOG", "ELEPHANT", "FROG", "GIRAFFE", "HOUSE", "IGLOO", "JUMP", "KITE", "LION", "MOUSE", "NIGHT"]

class WordChainRecruitView(discord.ui.View):
    def __init__(self, ctx):
        super().__init__(timeout=30)
        self.ctx = ctx
        self.players = [ctx.author]
        
    def update_embed(self, embed):
        embed.description = (
            f"Organized by {self.ctx.author.mention}\n\n"
            f"Players ({len(self.players)}/10):\n"
            f"{chr(10).join(['✅ ' + p.display_name for p in self.players])}\n\n"
            f"⏰ Starting in 30 seconds..."
        )

    @discord.ui.button(label="Join", style=discord.ButtonStyle.primary, emoji="🤝")
    async def join(self, interaction: discord.Interaction, button: discord.ui.Button):
        if interaction.user in self.players:
            await interaction.response.send_message("You're already in.", ephemeral=True)
            return
            
        if len(self.players) >= 10:
            await interaction.response.send_message("Lobby full.", ephemeral=True)
            return
            
        self.players.append(interaction.user)
        embed = interaction.message.embeds[0]
        self.update_embed(embed)
        
        if len(self.players) == 10:
            button.disabled = True
            await interaction.response.edit_message(embed=embed, view=self)
            self.stop()
        else:
            await interaction.response.edit_message(embed=embed, view=self)

async def start_wordchain(cog, ctx):
    embed = discord.Embed(title="🔗 Word Chain", color=0x3498DB)
    view = WordChainRecruitView(ctx)
    view.update_embed(embed)
    
    msg = await ctx.send(embed=embed, view=view)
    await view.wait()
    
    if len(view.players) < 2:
        await msg.edit(content="Not enough players. Word Chain cancelled.", embed=None, view=None)
        return
        
    active_players = view.players.copy()
    random.shuffle(active_players)
    
    for p in active_players:
        cog.active_games.add(p.id)
        
    try:
        used_words = set()
        current_word = random.choice(WORD_STARTS)
        used_words.add(current_word.lower())
        
        await ctx.send(f"🔗 Word Chain started!\nOrder: {', '.join([p.display_name for p in active_players])}\n\nFirst word: **{current_word}**")
        
        turn_index = 0
        
        while len(active_players) > 1:
            current_player = active_players[turn_index]
            last_char = current_word[-1].lower()
            
            def check(m):
                return m.author == current_player and m.channel == ctx.channel and not m.content.startswith('!')
                
            try:
                # 15 seconds to respond
                m = await cog.bot.wait_for('message', check=check, timeout=15.0)
                word = m.content.strip().lower()
                
                if ' ' in word:
                    word = word.split()[0] # take first word just in case
                    
                if not word.isalpha():
                    await ctx.send(f"❌ {current_player.mention}, letters only! Eliminated.")
                    active_players.remove(current_player)
                    if turn_index >= len(active_players): turn_index = 0
                    continue
                    
                if word[0] != last_char:
                    await ctx.send(f"❌ {current_player.mention}, '{word}' does not start with '{last_char.upper()}'! Eliminated.")
                    active_players.remove(current_player)
                    if turn_index >= len(active_players): turn_index = 0
                    continue
                    
                if word in used_words:
                    await ctx.send(f"❌ {current_player.mention}, '{word}' was already used! Eliminated.")
                    active_players.remove(current_player)
                    if turn_index >= len(active_players): turn_index = 0
                    continue
                    
                # Valid word
                used_words.add(word)
                current_word = word
                await m.add_reaction("✅")
                
                turn_index = (turn_index + 1) % len(active_players)
                
            except asyncio.TimeoutError:
                await ctx.send(f"⏰ {current_player.mention} took too long! Eliminated.")
                active_players.remove(current_player)
                if turn_index >= len(active_players): turn_index = 0
                
        # Game over
        winner = active_players[0]
        prize = random.randint(25, 50)
        update_wallet(winner.id, prize)
        
        await ctx.send(f"🏆 {winner.mention} is the last one standing and wins **{prize} 🪙**!")
        
    except Exception as e:
        await ctx.send("An error occurred.")
        raise e
    finally:
        for p in view.players:
            cog.active_games.discard(p.id)
