import discord
from discord.ext import commands
from datetime import timedelta
from utils.internal_commands import InternalCommandResult, InternalCommandExecutor
import logging

logger = logging.getLogger("bot")

class Moderation(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
    
    async def internal_mute(self, member, duration_minutes=5, reason=""):
        """Internal: Mute a member silently"""
        try:
            duration = timedelta(minutes=duration_minutes)
            await member.timeout(duration, reason=reason or "Automatic mute by bot")
            InternalCommandExecutor.log_action("Mute", f"{member.display_name} muted for {duration_minutes}m. Reason: {reason}")
            return InternalCommandResult(True, f"Muted {member.display_name}")
        except Exception as e:
            return InternalCommandResult(False, str(e))
    
    async def internal_unmute(self, member, reason=""):
        """Internal: Unmute a member silently"""
        try:
            await member.timeout(None, reason=reason or "Automatic unmute by bot")
            InternalCommandExecutor.log_action("Unmute", f"{member.display_name} unmuted. Reason: {reason}")
            return InternalCommandResult(True, f"Unmuted {member.display_name}")
        except Exception as e:
            return InternalCommandResult(False, str(e))

    @commands.command()
    @commands.guild_only()
    @commands.has_permissions(manage_messages=True)
    async def purge(self, ctx, amount: int):
        """Deletes the last X messages (1-100). Usage: !purge 20"""
        if amount < 1 or amount > 100:
            await ctx.send("❌ Choose a number between 1 and 100.", delete_after=5)
            return
        try:
            await ctx.channel.purge(limit=amount + 1)
        except discord.Forbidden:
            await ctx.send("❌ I don't have permission to delete messages here.", delete_after=5)
            return
        await ctx.send(f"🧹 Deleted **{amount}** messages.", delete_after=3)

    @commands.command()
    @commands.guild_only()
    @commands.has_permissions(kick_members=True)
    async def kick(self, ctx, member: discord.Member, *, reason="No reason provided"):
        """Kicks a member from the server."""
        if member.top_role >= ctx.author.top_role:
            await ctx.send("❌ You cannot kick someone with a higher or equal role.")
            return
        if member.top_role >= ctx.guild.me.top_role:
            await ctx.send("❌ I can't kick someone with a role higher than mine.")
            return
        try:
            await member.kick(reason=reason)
        except discord.Forbidden:
            await ctx.send("❌ I don't have permission to kick that member.")
            return
        await ctx.send(f"👢 **{member}** has been kicked. Reason: {reason}")

    @commands.command()
    @commands.guild_only()
    @commands.has_permissions(ban_members=True)
    async def ban(self, ctx, member: discord.Member, *, reason="No reason provided"):
        """Bans a member from the server."""
        if member.top_role >= ctx.author.top_role:
            await ctx.send("❌ You cannot ban someone with a higher or equal role.")
            return
        if member.top_role >= ctx.guild.me.top_role:
            await ctx.send("❌ I can't ban someone with a role higher than mine.")
            return
        try:
            await member.ban(reason=reason)
        except discord.Forbidden:
            await ctx.send("❌ I don't have permission to ban that member.")
            return
        await ctx.send(f"🔨 **{member}** has been banned. Reason: {reason}")

    @commands.command()
    @commands.guild_only()
    @commands.has_permissions(moderate_members=True)
    async def mute(self, ctx, member: discord.Member):
        """Times out a member for 5 minutes (Mute)."""
        duration = timedelta(minutes=5)
        try:
            await member.timeout(duration, reason="Muted by command")
        except discord.Forbidden:
            await ctx.send("❌ I can't mute that member (their role may be higher than mine).")
            return
        await ctx.send(f"😶 **{member}** has been muted for 5 minutes.")

    @commands.command()
    @commands.guild_only()
    @commands.has_permissions(moderate_members=True)
    async def unmute(self, ctx, member: discord.Member):
        """Removes timeout."""
        try:
            await member.timeout(None)
        except discord.Forbidden:
            await ctx.send("❌ I can't unmute that member.")
            return
        await ctx.send(f"🗣️ **{member}** has been unmuted.")

    @commands.command()
    @commands.guild_only()
    async def userinfo(self, ctx, member: discord.Member = None):
        """Displays info about a user."""
        member = member or ctx.author

        embed = discord.Embed(title=f"User Info: {member}", color=member.color)
        embed.set_thumbnail(url=member.display_avatar.url)
        embed.add_field(name="ID", value=member.id, inline=True)
        embed.add_field(name="Created Account", value=member.created_at.strftime("%Y-%m-%d"), inline=True)
        joined = member.joined_at.strftime("%Y-%m-%d") if member.joined_at else "Unknown"
        embed.add_field(name="Joined Server", value=joined, inline=True)
        
        roles = [role.mention for role in member.roles if role.name != "@everyone"]
        embed.add_field(name=f"Roles ({len(roles)})", value=" ".join(roles) if roles else "None", inline=False)
        
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Moderation(bot))