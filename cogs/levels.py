import discord
from discord.ext import commands
import random
import time
import logging

from utils.database import db
from utils.config import get_guild_config

logger = logging.getLogger("bot")


# ========== DB HELPERS ==========

def get_level(user_id, guild_id="global"):
    """Get user level data from DB. Creates entry if missing."""
    uid = str(user_id)
    gid = str(guild_id)
    db.cursor.execute("SELECT xp, level FROM levels WHERE user_id = ? AND guild_id = ?", (uid, gid))
    row = db.cursor.fetchone()
    if not row:
        # Check if there's migrated 'global' data (from levels.json migration)
        if gid != 'global':
            db.cursor.execute("SELECT xp, level FROM levels WHERE user_id = ? AND guild_id = 'global'", (uid,))
            global_row = db.cursor.fetchone()
            if global_row:
                # Copy global data to the guild-specific entry
                db.cursor.execute(
                    "INSERT INTO levels (user_id, guild_id, xp, level) VALUES (?, ?, ?, ?)",
                    (uid, gid, global_row[0], global_row[1])
                )
                db.connection.commit()
                logger.info(f"[Levels] Migrated user {uid} from global to guild {gid} (Level {global_row[1]})")
                return {"xp": global_row[0], "level": global_row[1]}
        db.cursor.execute(
            "INSERT INTO levels (user_id, guild_id, xp, level) VALUES (?, ?, 0, 1)",
            (uid, gid)
        )
        db.connection.commit()
        return {"xp": 0, "level": 1}
    return {"xp": row[0], "level": row[1]}


def set_level(user_id, guild_id="global", level=None, xp=None):
    """Set user level/xp in DB."""
    uid = str(user_id)
    gid = str(guild_id)
    # Ensure entry exists
    get_level(user_id, guild_id)
    parts = []
    values = []
    if level is not None:
        parts.append("level = ?")
        values.append(level)
    if xp is not None:
        parts.append("xp = ?")
        values.append(xp)
    if not parts:
        return
    values.extend([uid, gid])
    db.cursor.execute(f"UPDATE levels SET {', '.join(parts)} WHERE user_id = ? AND guild_id = ?", values)
    db.connection.commit()


def get_xp_cap(level):
    return 5 * (level ** 2) + (50 * level) + 100


def add_xp(user_id, guild_id="global", amount=0):
    """Add XP to a user. Returns (new_level, leveled_up)."""
    data = get_level(user_id, guild_id)
    new_xp = data["xp"] + amount
    level = data["level"]
    leveled_up = False

    cap = get_xp_cap(level)
    if new_xp >= cap:
        level += 1
        leveled_up = True
        # Don't reset XP, just let it accumulate (carry over)

    uid = str(user_id)
    gid = str(guild_id)
    db.cursor.execute(
        "UPDATE levels SET xp = ?, level = ? WHERE user_id = ? AND guild_id = ?",
        (new_xp, level, uid, gid)
    )
    db.connection.commit()
    return level, leveled_up


class Levels(commands.Cog):
    def __init__(self, bot):
        self.bot = bot
        self.cooldowns = {}

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

        xp_gain = random.randint(15, 25)
        new_level, leveled_up = add_xp(message.author.id, message.guild.id, xp_gain)

        if leveled_up:
            guild_id = str(message.guild.id)
            channel_id = get_guild_config(guild_id, "announce_channel")

            level_msg = f"🎉 {message.author.mention} has leveled up to **Level {new_level}**!"

            if channel_id:
                channel = self.bot.get_channel(int(channel_id))
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
        data = get_level(member.id, ctx.guild.id if ctx.guild else "global")

        lvl = data["level"]
        xp = data["xp"]
        cap = get_xp_cap(lvl)

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
        guild_id = str(ctx.guild.id) if ctx.guild else "global"
        db.cursor.execute(
            "SELECT user_id, xp, level FROM levels WHERE guild_id = ? ORDER BY level DESC, xp DESC LIMIT 10",
            (guild_id,)
        )
        rows = db.cursor.fetchall()
        
        desc = ""
        for i, row in enumerate(rows, start=1):
            member = ctx.guild.get_member(int(row[0])) if ctx.guild else None
            name = member.display_name if member else f"User {row[0]}"
            desc += f"**{i}.** {name} - Lvl {row[2]} ({row[1]} XP)\n"

        embed = discord.Embed(title="🏆 Server Leaderboard", description=desc or "No data yet.", color=0xFFD700)
        await ctx.send(embed=embed)


async def setup(bot):
    await bot.add_cog(Levels(bot))