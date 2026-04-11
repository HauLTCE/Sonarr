from discord.ext import commands
import os
from datetime import datetime, timezone, timedelta

class WrongChannelError(commands.CheckFailure):
    def __init__(self, channel_id):
        self.channel_id = channel_id

class BotRestrictedTimeError(commands.CheckFailure):
    """Raised when bot is in restricted time (sleep or lunch break)"""
    def __init__(self, reason: str):
        self.reason = reason

def is_sleep_time():
    """Check if bot is in sleep mode (10PM - 6AM in UTC+7)"""
    utc_plus_7 = timezone(timedelta(hours=7))
    now = datetime.now(utc_plus_7)
    hour = now.hour
    return hour >= 22 or hour < 6

def is_lunch_break():
    """Check if bot is on lunch break (12PM - 1PM in UTC+7)"""
    enabled = os.getenv("MIDDAY_BREAK_ENABLED", "True").lower() == "true"
    if not enabled:
        return False
    utc_plus_7 = timezone(timedelta(hours=7))
    now = datetime.now(utc_plus_7)
    return now.hour == 12

def is_restricted_time():
    """Check if bot should restrict economy commands (sleep or lunch)"""
    return is_sleep_time() or is_lunch_break()

def get_restriction_reason():
    """Get the reason for restriction"""
    if is_sleep_time():
        return "The bot is asleep. Economy commands are disabled until 6AM."
    elif is_lunch_break():
        return "The bot is on lunch break. Economy commands are disabled until 1PM."
    return None

def economy_allowed():
    """Check decorator - ensures economy commands only run outside restricted hours"""
    async def predicate(ctx):
        if is_restricted_time():
            reason = get_restriction_reason()
            raise BotRestrictedTimeError(reason)
        return True
    return commands.check(predicate)

def is_music_channel():
    async def predicate(ctx):
        if not ctx.guild:
            return True
        
        guild_id = str(ctx.guild.id)
        config = ctx.bot.server_config.get(guild_id, {})
        music_channel_id = config.get("music_channel")

        if not music_channel_id:
            return True
        
        if ctx.channel.id == music_channel_id:
            return True
        
        raise WrongChannelError(music_channel_id)
    return commands.check(predicate)