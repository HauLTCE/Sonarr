import discord
from discord.ext import commands, tasks
import json
import os
import random
import time
import logging

logger = logging.getLogger("bot")

LEVELS_FILE = "levels.json"

class Levels(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.users = {}
        self.load_data()
        self.cooldowns = {}
        self.save_data_loop.start()

    def cog_unload(self):
        self.save_data_loop.cancel()
        self.save_data()

    def load_data(self):
        if os.path.exists(LEVELS_FILE):
            try:
                with open(LEVELS_FILE, "r") as f:
                    self.users = json.load(f)
            except Exception:
                self.users = {}

    def save_data(self):
        try:
            with open(LEVELS_FILE, "w") as f:
                json.dump(self.users, f)
        except Exception as e:
            logger.error(f"Failed to save levels: {e}")

    @tasks.loop(minutes=5)
    async def save_data_loop(self):
        self.save_data()

    def get_xp_cap(self, level):
        return 5 * (level ** 2) + (50 * level) + 100

    @commands.Cog.listener()
    async def on_message(self, message):
        if message.author.bot or not message.guild:
            return
        
        if self.bot.user in message.mentions:
            return

        user_id = str(message.author.id)
        
        if user_id in self.cooldowns:
            if time.time() - self.cooldowns[user_id] < 60:
                return

        self.cooldowns[user_id] = time.time()

        if user_id not in self.users:
            self.users[user_id] = {"xp": 0, "level": 1}

        xp_gain = random.randint(15, 25)
        self.users[user_id]["xp"] += xp_gain
        
        current_xp = self.users[user_id]["xp"]
        current_lvl = self.users[user_id]["level"]
        xp_cap = self.get_xp_cap(current_lvl)

        if current_xp >= xp_cap:
            self.users[user_id]["level"] += 1

            guild_id = str(message.guild.id)
            config = self.bot.server_config.get(guild_id, {})
            channel_id = config.get("announce_channel") or config.get("welcome_channel")

            level_msg = f"🎉 {message.author.mention} has leveled up to **Level {self.users[user_id]['level']}**!"

            if channel_id:
                try:
                    cid = int(channel_id)
                except (TypeError, ValueError):
                    cid = None
                channel = self.bot.get_channel(cid) if cid else None
                if channel:
                    await channel.send(level_msg)
                else:
                    await message.channel.send(level_msg)
            else:
                await message.channel.send(level_msg)

    @commands.command(aliases=['rank', 'lvl'])
    async def level(self, ctx, member: discord.Member = None):
        """Check your current level and XP progress."""
        member = member or ctx.author
        user_id = str(member.id)

        if user_id not in self.users:
            await ctx.send("User has no XP yet!")
            return

        lvl = self.users[user_id]["level"]
        xp = self.users[user_id]["xp"]
        cap = self.get_xp_cap(lvl)

        embed = discord.Embed(title=f"📊 Rank: {member.display_name}", color=0x3498db)
        embed.set_thumbnail(url=member.display_avatar.url)
        embed.add_field(name="Level", value=str(lvl), inline=True)
        embed.add_field(name="XP", value=f"{xp} / {cap}", inline=True)
        
        percent = min(xp / cap, 1.0)
        bar_len = 20
        filled = int(percent * bar_len)
        bar = "█" * filled + "░" * (bar_len - filled)
        embed.add_field(name="Progress", value=f"{bar}", inline=False)

        await ctx.send(embed=embed)

    @commands.command(aliases=['top'])
    async def leaderboard(self, ctx):
        """Display the top 10 users with the most XP."""
        sorted_users = sorted(self.users.items(), key=lambda x: x[1]['xp'], reverse=True)[:10]
        
        desc = ""
        for i, (uid, data) in enumerate(sorted_users, start=1):
            member = ctx.guild.get_member(int(uid))
            name = member.display_name if member else f"User {uid}"
            desc += f"**{i}.** {name} - Lvl {data['level']} ({data['xp']} XP)\n"

        embed = discord.Embed(title="🏆 Server Leaderboard", description=desc, color=0xFFD700)
        await ctx.send(embed=embed)

async def setup(bot):
    await bot.add_cog(Levels(bot))