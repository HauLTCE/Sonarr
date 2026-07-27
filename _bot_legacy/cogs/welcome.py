import discord
from discord.ext import commands
import logging

logger = logging.getLogger("bot")

class Welcome(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    @commands.Cog.listener()
    async def on_member_join(self, member):
        guild_id = str(member.guild.id)
        config = self.bot.server_config.get(guild_id, {})
        channel_id = config.get("welcome_channel")

        if not channel_id:
            return
        try:
            cid = int(channel_id)
        except (TypeError, ValueError):
            cid = None

        channel = self.bot.get_channel(cid) if cid else None
        if channel:
            embed = discord.Embed(
                title="Welcome to the Server!",
                description=f"👋 Hello {member.mention}, welcome to **{member.guild.name}**! We are glad to have you.",
                color=0x00ff00
            )
            embed.set_thumbnail(url=member.display_avatar.url)
            embed.set_footer(text=f"Member #{len(member.guild.members)}")
            await channel.send(embed=embed)
        else:
            logger.warning(f"Welcome channel for guild {guild_id} not found (id={channel_id}). Skipping welcome message.")

    @commands.Cog.listener()
    async def on_member_remove(self, member):
        guild_id = str(member.guild.id)
        config = self.bot.server_config.get(guild_id, {})
        channel_id = config.get("welcome_channel")

        if not channel_id:
            return

        try:
            cid = int(channel_id)
        except (TypeError, ValueError):
            cid = None

        channel = self.bot.get_channel(cid) if cid else None
        if channel:
            embed = discord.Embed(
                description=f"🚪 **{member.display_name}** has left the server.",
                color=0xff0000
            )
            await channel.send(embed=embed)
        else:
            logger.warning(f"Welcome channel for guild {guild_id} not found (id={channel_id}). Skipping farewell message.")

async def setup(bot):
    await bot.add_cog(Welcome(bot))