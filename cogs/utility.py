import discord
from discord.ext import commands
import time
import psutil
import platform
import asyncio
from utils.internal_commands import InternalCommandResult, InternalCommandExecutor
import logging

logger = logging.getLogger("bot")

class Utility(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.start_time = time.time()
    
    def internal_ping(self):
        """Internal: Get bot ping silently"""
        ping = round(self.bot.latency * 1000)
        InternalCommandExecutor.log_action("Ping", f"{ping}ms")
        return InternalCommandResult(True, f"Pong! {ping}ms", data={"ping": ping})
    
    async def internal_remind(self, user_id, seconds, task):
        """Internal: Remind a user after delay"""
        try:
            InternalCommandExecutor.log_action("Reminder Set", f"User {user_id}: {task} in {seconds}s")
            await asyncio.sleep(seconds)
            user = self.bot.get_user(user_id)
            if user:
                await user.send(f"🔔 Reminder: **{task}**")
                InternalCommandExecutor.log_action("Reminder Sent", f"User {user_id}: {task}")
            return InternalCommandResult(True, "Reminder sent")
        except Exception as e:
            return InternalCommandResult(False, str(e))

    @commands.command()
    async def ping(self, ctx):
        """Checks the bot's latency."""
        await ctx.send(f'Pong! ({round(self.bot.latency * 1000)}ms)')

    @commands.command()
    async def echo(self, ctx, *, message):
        """Repeats the message you sent."""
        await ctx.send(message)

    @commands.command()
    async def status(self, ctx):
        """Displays detailed bot and system vitals."""
        current_time = time.time()
        uptime_seconds = int(current_time - self.start_time)
        m, s = divmod(uptime_seconds, 60)
        h, m = divmod(m, 60)
        d, h = divmod(h, 24)
        uptime_str = f"{d}d {h}h {m}m {s}s"

        cpu_usage = psutil.cpu_percent()
        ram = psutil.virtual_memory()
        ram_used = round(ram.used / (1024.0 ** 3), 2)
        ram_total = round(ram.total / (1024.0 ** 3), 2)
        ram_percent = ram.percent

        proc = psutil.Process()
        bot_ram = round(proc.memory_info().rss / (1024.0 ** 2), 2)
        
        ping = round(self.bot.latency * 1000)
        total_servers = len(self.bot.guilds)
        total_users = sum(guild.member_count for guild in self.bot.guilds)

        python_ver = platform.python_version()
        discord_ver = discord.__version__

        embed = discord.Embed(title="📊 System Status & Vitals", color=0x00ff00)
        
        embed.add_field(name="🤖 Bot Vitals", value=(
            f"**Ping:** `{ping}ms`\n"
            f"**Uptime:** `{uptime_str}`\n"
            f"**RAM (Bot):** `{bot_ram} MB`"
        ), inline=True)

        embed.add_field(name="🌍 Scope", value=(
            f"**Servers:** `{total_servers}`\n"
            f"**Users:** `{total_users}`"
        ), inline=True)

        embed.add_field(name="🖥️ Hardware", value=(
            f"**CPU Usage:** `{cpu_usage}%`\n"
            f"**RAM (Sys):** `{ram_used}GB / {ram_total}GB ({ram_percent}%)`\n"
            f"**OS:** `{platform.system()} {platform.release()}`"
        ), inline=False)

        embed.add_field(name="📚 Versions", value=(
            f"**Python:** `{python_ver}` | **Discord.py:** `{discord_ver}`"
        ), inline=False)

        avatar_url = ctx.author.avatar.url if ctx.author.avatar else None
        embed.set_footer(text=f"Requested by {ctx.author}", icon_url=avatar_url)
        embed.timestamp = ctx.message.created_at
        
        await ctx.send(embed=embed)

    @commands.command()
    async def remind(self, ctx, time_str: str, *, task: str):
        """Sets a reminder. Format: !remind 10s/10m/1h [Task]"""
        def convert(time_str):
            pos = ['s', 'm', 'h', 'd']
            time_dict = {"s": 1, "m": 60, "h": 3600, "d": 3600*24}
            unit = time_str[-1]
            if unit not in pos:
                return -1
            try:
                val = int(time_str[:-1])
            except ValueError:
                return -1
            return val * time_dict[unit]

        seconds = convert(time_str)
        if seconds == -1:
            await ctx.send("❌ Invalid format. Use 10s, 5m, 1h, etc.")
            return

        await ctx.send(f"⏰ Timer set for **{task}** in **{time_str}**.")
        await asyncio.sleep(seconds)
        await ctx.send(f"🔔 {ctx.author.mention}, you asked me to remind you: **{task}**")

async def setup(bot):
    await bot.add_cog(Utility(bot))