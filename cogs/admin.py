import discord
from discord.ext import commands
import json
import os

from utils.economy import EconomyManager

CONFIG_FILE = "server_config.json"

class Admin(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.economy_manager = EconomyManager()

    def save_config(self):
        with open(CONFIG_FILE, "w") as f:
            json.dump(self.bot.server_config, f)

    @commands.group(invoke_without_command=True)
    @commands.has_permissions(administrator=True)
    async def setchannel(self, ctx):
        """Sets designated channels. Usage: !setchannel music | announce | welcome | general"""
        await ctx.send("Usage: `!setchannel music`, `!setchannel announce`, `!setchannel welcome`, or `!setchannel general`")

    @setchannel.command(name="music")
    async def set_music(self, ctx):
        """Sets the current channel as the Music Channel."""
        guild_id = str(ctx.guild.id)
        if guild_id not in self.bot.server_config:
            self.bot.server_config[guild_id] = {}
        
        self.bot.server_config[guild_id]["music_channel"] = ctx.channel.id
        self.save_config()
        await ctx.send(f"✅ **{ctx.channel.mention}** is now the designated Music Channel.")

    @setchannel.command(name="announce")
    async def set_announce(self, ctx):
        """Sets the current channel as the Announcement Channel."""
        guild_id = str(ctx.guild.id)
        if guild_id not in self.bot.server_config:
            self.bot.server_config[guild_id] = {}
        
        self.bot.server_config[guild_id]["announce_channel"] = ctx.channel.id
        self.save_config()
        await ctx.send(f"✅ **{ctx.channel.mention}** is now the designated Announcement Channel.")

    @setchannel.command(name="market")
    async def set_market(self, ctx):
        """Sets the current channel for Market announcements (stock updates, news, bankruptcies)."""
        from utils.database import db
        guild_id = str(ctx.guild.id)
        db.set_market_channel(guild_id, ctx.channel.id)
        await ctx.send(f"✅ **{ctx.channel.mention}** is now the designated Market Channel. I'll announce stock updates, news, and bankruptcies here.")

    @setchannel.command(name="welcome")
    async def set_welcome(self, ctx):
        """Sets the current channel for Welcome/Goodbye messages."""
        guild_id = str(ctx.guild.id)
        if guild_id not in self.bot.server_config:
            self.bot.server_config[guild_id] = {}
        
        self.bot.server_config[guild_id]["welcome_channel"] = ctx.channel.id
        self.save_config()
        await ctx.send(f"👋 **{ctx.channel.mention}** is now the designated Welcome Channel.")

    @setchannel.command(name="general")
    async def set_general(self, ctx):
        """Sets the current channel as the General Channel for bot messages."""
        guild_id = str(ctx.guild.id)
        if guild_id not in self.bot.server_config:
            self.bot.server_config[guild_id] = {}
        
        self.bot.server_config[guild_id]["general_channel"] = ctx.channel.id
        self.save_config()
        await ctx.send(f"💬 **{ctx.channel.mention}** is now the designated General Channel.")

    @commands.command()
    @commands.has_permissions(administrator=True)
    async def announce(self, ctx, title: str, *, message: str):
        """Sends an announcement. Usage: !announce "Title" Message content"""
        guild_id = str(ctx.guild.id)
        config = self.bot.server_config.get(guild_id, {})
        channel_id = config.get("announce_channel")

        if not channel_id:
            await ctx.send("❌ No announcement channel set! Use `!setchannel announce` first.")
            return

        channel = self.bot.get_channel(channel_id)
        if not channel:
            await ctx.send("❌ The announcement channel no longer exists.")
            return

        embed = discord.Embed(title=f"📢 {title}", description=message, color=0xff0000)
        embed.set_footer(text=f"Sent by {ctx.author.display_name}")
        
        await channel.send(embed=embed)
        await ctx.message.delete()
        await ctx.send(f"✅ Announcement sent to {channel.mention}", delete_after=5)

    @commands.command(name="admin_addmoney", hidden=True)
    @commands.has_permissions(administrator=True)
    async def admin_addmoney(self, ctx, amount: int, location: str = "wallet"):
        """Add money to your own balance (admin only)."""
        if amount <= 0:
            return await ctx.send("❌ Amount must be positive.")
        location = location.lower()
        if location not in ["wallet", "bank"]:
            return await ctx.send("❌ Location must be 'wallet' or 'bank'.")
        self.economy_manager.update_balance(ctx.author.id, amount, location)
        await ctx.send(f"✅ Added ${amount} to your {location}.")

async def setup(bot):
    await bot.add_cog(Admin(bot))
