import discord
from discord.ext import commands
from utils.config import get_guild_config, set_guild_config


class Admin(commands.Cog):
    def __init__(self, bot):
        self.bot = bot

    async def cog_check(self, ctx):
        """Require Administrator for every command in this cog.

        The `setchannel` group uses invoke_without_command=True, and discord.py
        skips a group's own checks for its sub-commands in that mode. The
        sub-commands (`economy`, `games`, `clear`, ...) had no checks of their
        own, so any member could reconfigure or wipe a guild's channel config.
        A cog_check DOES run for sub-commands, so gate the whole cog here.
        """
        if ctx.guild is None:
            raise commands.NoPrivateMessage()
        perms = ctx.author.guild_permissions
        if not perms.administrator:
            raise commands.MissingPermissions(['administrator'])
        return True

    @commands.group(invoke_without_command=True)
    @commands.has_permissions(administrator=True)
    async def setchannel(self, ctx):
        """Sets designated channels. Usage: !setchannel <type>"""
        embed = discord.Embed(title="📌 Channel Configuration", color=0x3498DB)
        embed.description = (
            "Set channels for different bot functions:\n\n"
            "**Core Channels:**\n"
            "`!setchannel music` — Music commands\n"
            "`!setchannel announce` — Announcements\n"
            "`!setchannel welcome` — Welcome/goodbye messages\n"
            "`!setchannel general` — General bot messages\n\n"
            "**Game Channels:**\n"
            "`!setchannel games` — Casino/gambling commands\n"
            "`!setchannel dungeon` — Adventure commands\n"
            "`!setchannel economy` — Economy commands (bal, daily, work)\n"
            "`!setchannel logs` — Bot event logs (kills, drops, heists)\n\n"
            "`!setchannel view` — View current config\n"
            "`!setchannel clear <type>` — Remove a channel setting"
        )
        await ctx.send(embed=embed)

    async def _set_channel(self, ctx, key: str, label: str, emoji: str):
        set_guild_config(ctx.guild.id, key, ctx.channel.id)
        # Also update in-memory config for backwards compat
        guild_id = str(ctx.guild.id)
        if guild_id not in self.bot.server_config:
            self.bot.server_config[guild_id] = {}
        self.bot.server_config[guild_id][key] = ctx.channel.id
        await ctx.send(f"{emoji} **{ctx.channel.mention}** is now the designated {label}.")

    @setchannel.command(name="music")
    async def set_music(self, ctx):
        """Sets the Music Channel."""
        await self._set_channel(ctx, "music_channel", "Music Channel", "🎵")

    @setchannel.command(name="announce")
    async def set_announce(self, ctx):
        """Sets the Announcement Channel."""
        await self._set_channel(ctx, "announce_channel", "Announcement Channel", "📢")

    @setchannel.command(name="welcome")
    async def set_welcome(self, ctx):
        """Sets the Welcome Channel."""
        await self._set_channel(ctx, "welcome_channel", "Welcome Channel", "👋")

    @setchannel.command(name="general")
    async def set_general(self, ctx):
        """Sets the General Channel."""
        await self._set_channel(ctx, "general_channel", "General Channel", "💬")

    @setchannel.command(name="games")
    async def set_games(self, ctx):
        """Sets the Games/Gambling Channel."""
        await self._set_channel(ctx, "games_channel", "Games Channel", "🎰")

    @setchannel.command(name="dungeon")
    async def set_dungeon(self, ctx):
        """Sets the Dungeon/Adventure Channel."""
        await self._set_channel(ctx, "dungeon_channel", "Dungeon Channel", "🗡️")

    @setchannel.command(name="economy")
    async def set_economy(self, ctx):
        """Sets the Economy Channel."""
        await self._set_channel(ctx, "economy_channel", "Economy Channel", "💰")


    @setchannel.command(name="logs")
    async def set_logs(self, ctx):
        """Sets the Bot Logs Channel (kills, drops, heists)."""
        await self._set_channel(ctx, "logs_channel", "Logs Channel", "📋")

    @setchannel.command(name="view")
    async def view_config(self, ctx):
        """View all channel configurations."""
        guild_id = str(ctx.guild.id)
        from utils.database import db
        db.cursor.execute("SELECT config_key, config_value FROM guild_config WHERE guild_id = ?", (guild_id,))
        rows = db.cursor.fetchall()

        if not rows:
            await ctx.send("No channels configured yet.")
            return

        embed = discord.Embed(title="📌 Channel Configuration", color=0x3498DB)
        desc = ""
        for row in rows:
            key = row[0]
            try:
                ch = self.bot.get_channel(int(row[1]))
                ch_str = ch.mention if ch else f"(deleted: {row[1]})"
            except (ValueError, TypeError):
                ch_str = str(row[1])
            desc += f"**{key}:** {ch_str}\n"
        embed.description = desc
        await ctx.send(embed=embed)

    @setchannel.command(name="clear")
    async def clear_channel(self, ctx, channel_type: str):
        """Remove a channel setting."""
        from utils.database import db
        key = f"{channel_type}_channel" if not channel_type.endswith("_channel") else channel_type
        db.cursor.execute("DELETE FROM guild_config WHERE guild_id = ? AND config_key = ?", (str(ctx.guild.id), key))
        db.connection.commit()
        # Remove from memory too
        guild_id = str(ctx.guild.id)
        if guild_id in self.bot.server_config and key in self.bot.server_config[guild_id]:
            del self.bot.server_config[guild_id][key]
        await ctx.send(f"✅ Cleared `{key}` setting.")

    @commands.command()
    @commands.has_permissions(administrator=True)
    async def announce(self, ctx, title: str, *, message: str):
        """Sends an announcement. Usage: !announce "Title" Message content"""
        channel_id = get_guild_config(ctx.guild.id, "announce_channel")
        if not channel_id:
            await ctx.send("❌ No announcement channel set! Use `!setchannel announce` first.")
            return
        channel = self.bot.get_channel(int(channel_id))
        if not channel:
            await ctx.send("❌ The announcement channel no longer exists.")
            return
        embed = discord.Embed(title=f"📢 {title}", description=message, color=0xff0000)
        embed.set_footer(text=f"Sent by {ctx.author.display_name}")
        await channel.send(embed=embed)
        await ctx.message.delete()
        await ctx.send(f"✅ Announcement sent to {channel.mention}", delete_after=5)


async def setup(bot):
    await bot.add_cog(Admin(bot))
