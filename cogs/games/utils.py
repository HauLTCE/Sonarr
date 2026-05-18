import discord
from utils.economy_helpers import get_balance
from utils.config import get_guild_config

async def validate_bet(ctx, bet: int, min_bet=10, max_bet=5000) -> bool:
    """
    Universal bet validator. Call before starting any game.
    Returns True if bet is valid, sends error message and returns False otherwise.
    """
    if bet < min_bet:
        await ctx.send(f"Minimum bet is {min_bet} coins. Don't waste my time.")
        return False
    if bet > max_bet:
        await ctx.send(f"Max bet is {max_bet} coins. I'm not a high roller table.")
        return False
    
    balance = get_balance(ctx.author.id)
    if balance['wallet'] < bet:
        await ctx.send(f"You have {balance['wallet']} coins. You're trying to bet {bet}. Math isn't your thing.")
        return False
    
    return True


async def check_channel(ctx, channel_type: str) -> bool:
    """
    Check if the command is being used in the correct channel.
    channel_type: 'games_channel', 'dungeon_channel', 'economy_channel'
    Returns True if OK, False if wrong channel (sends redirect + deletes).
    If no channel is set, always returns True (backwards-compatible).
    """
    if not ctx.guild:
        return True
    
    required_channel_id = get_guild_config(ctx.guild.id, channel_type)
    if not required_channel_id:
        return True  # No restriction set
    
    if ctx.channel.id == int(required_channel_id):
        return True
    
    channel = ctx.guild.get_channel(int(required_channel_id))
    if channel:
        await ctx.send(f"Wrong channel. Go to {channel.mention} for this.", delete_after=5)
    else:
        await ctx.send("The configured channel no longer exists. Ask an admin to reset it.", delete_after=5)
    
    try:
        await ctx.message.delete(delay=3)
    except Exception:
        pass
    
    return False
