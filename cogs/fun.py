import discord
from discord.ext import commands
import random

class Fun(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @commands.command()
    async def roll(self, ctx, dice: str):
        """Rolls dice in NdN format (e.g. !roll 1d20)."""
        try:
            rolls, limit = map(int, dice.split('d'))
        except Exception:
            await ctx.send('Format has to be in NdN! (e.g. 1d20, 2d6)')
            return

        result = ', '.join(str(random.randint(1, limit)) for r in range(rolls))
        await ctx.send(f'🎲 **{result}**')

    @commands.command()
    async def poll(self, ctx, *, question):
        """Creates a simple Yes/No poll."""
        embed = discord.Embed(title="📊 Poll", description=f"**{question}**", color=0x00ff00)
        embed.set_footer(text=f"Poll started by {ctx.author.display_name}")
        message = await ctx.send(embed=embed)
        await message.add_reaction("✅")
        await message.add_reaction("❌")

    @commands.command(aliases=['8ball'])
    async def eightball(self, ctx, *, question):
        """Ask the magic 8-ball a question."""
        responses = [
            "It is certain.", "It is decidedly so.", "Without a doubt.",
            "Yes - definitely.", "You may rely on it.", "As I see it, yes.",
            "Most likely.", "Outlook good.", "Yes.", "Signs point to yes.",
            "Reply hazy, try again.", "Ask again later.", "Better not tell you now.",
            "Cannot predict now.", "Concentrate and ask again.",
            "Don't count on it.", "My reply is no.", "My sources say no.",
            "Outlook not so good.", "Very doubtful."
        ]
        await ctx.send(f"🎱 **Question:** {question}\n**Answer:** {random.choice(responses)}")

async def setup(bot):
    await bot.add_cog(Fun(bot))