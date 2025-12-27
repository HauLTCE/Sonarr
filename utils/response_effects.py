"""
Response Effects Handler
Parses special prefixes in responses and applies effects like timeouts, robbery, etc.

Supported prefixes:
- TIMEOUT:message - Apply 1 hour timeout
- TIMEOUT:30m:message - Apply custom duration timeout
- ROB:message - Rob 5% from user's wallet
- ROB:10:message - Rob custom % from user's wallet
- GOOGLE:message - Add a Google search link with their query
- (More can be added later: RECURSIVE:, REACT:, DM:, etc.)
"""

import re
import logging
import urllib.parse
from datetime import timedelta
from dataclasses import dataclass, field
from typing import Optional, Tuple, List

logger = logging.getLogger("bot")


@dataclass
class ResponseEffect:
    """Container for parsed response effects."""
    message: str
    timeout_duration: Optional[timedelta] = None
    rob_percent: Optional[float] = None  # e.g., 0.05 for 5%
    google_query: Optional[str] = None  # Query to create Google search link
    # Future effects can be added here:
    # reactions: List[str] = None
    # dm_message: str = None
    # recursive_category: str = None


def parse_duration(duration_str: str) -> Optional[timedelta]:
    """
    Parse duration string into timedelta.
    Supports: 30s, 5m, 1h, 2h, 1d
    """
    if not duration_str:
        return None
    
    duration_str = duration_str.lower().strip()
    
    match = re.match(r'^(\d+)([smhd])$', duration_str)
    if not match:
        return None
    
    value = int(match.group(1))
    unit = match.group(2)
    
    if unit == 's':
        return timedelta(seconds=value)
    elif unit == 'm':
        return timedelta(minutes=value)
    elif unit == 'h':
        return timedelta(hours=value)
    elif unit == 'd':
        return timedelta(days=value)
    
    return None


def parse_response(response: str) -> ResponseEffect:
    """
    Parse a response string for special effect prefixes.
    Multiple prefixes can be chained.
    
    Examples:
        "Hello there" -> ResponseEffect(message="Hello there")
        "TIMEOUT:You earned a timeout" -> ResponseEffect(message="You earned...", timeout_duration=1h)
        "TIMEOUT:30m:Short timeout" -> ResponseEffect(message="Short timeout", timeout_duration=30m)
        "ROB:Took your money" -> ResponseEffect(message="Took your money", rob_percent=0.05)
        "ROB:10:Took 10%" -> ResponseEffect(message="Took 10%", rob_percent=0.10)
        "GOOGLE:Go search it" -> ResponseEffect(message="Go search it", google_query=original_user_query)
    """
    effect = ResponseEffect(message=response)
    remaining = response
    
    # Parse TIMEOUT: prefix
    if remaining.startswith("TIMEOUT:"):
        remaining = remaining[8:]
        duration_match = re.match(r'^(\d+[smhd]):(.+)$', remaining, re.DOTALL)
        
        if duration_match:
            duration_str = duration_match.group(1)
            remaining = duration_match.group(2)
            effect.timeout_duration = parse_duration(duration_str)
        else:
            effect.timeout_duration = timedelta(hours=1)
    
    # Parse ROB: prefix
    if remaining.startswith("ROB:"):
        remaining = remaining[4:]
        percent_match = re.match(r'^(\d+):(.+)$', remaining, re.DOTALL)
        
        if percent_match:
            percent = int(percent_match.group(1))
            remaining = percent_match.group(2)
            effect.rob_percent = percent / 100.0
        else:
            effect.rob_percent = 0.05  # Default 5%
    
    # Parse GOOGLE: prefix
    if remaining.startswith("GOOGLE:"):
        remaining = remaining[7:]
        effect.google_query = True  # Flag to use user's query
    
    effect.message = remaining
    return effect


async def apply_effects(effect: ResponseEffect, message, user_query: str = None, logger_prefix: str = "[EFFECT]") -> str:
    """
    Apply all effects from a parsed response to a Discord message.
    
    Args:
        effect: Parsed ResponseEffect object
        message: Discord message object (for author, channel, etc.)
        user_query: Original user query (for GOOGLE: effect)
        logger_prefix: Prefix for log messages
    
    Returns:
        The cleaned message text to send
    """
    import discord
    from utils.economy import EconomyManager
    
    final_message = effect.message
    
    # Apply timeout effect
    if effect.timeout_duration:
        try:
            await message.author.timeout(
                effect.timeout_duration, 
                reason="Bot response effect triggered"
            )
            duration_str = format_duration(effect.timeout_duration)
            logger.warning(f"{logger_prefix} Timed out {message.author} for {duration_str}")
        except discord.Forbidden:
            logger.warning(f"{logger_prefix} Could not timeout {message.author} - missing permissions")
        except Exception as e:
            logger.error(f"{logger_prefix} Timeout error: {e}")
    
    # Apply rob effect
    if effect.rob_percent:
        try:
            economy = EconomyManager()
            user_id = str(message.author.id)
            bot_id = str(message.guild.me.id) if message.guild else None
            
            if bot_id:
                wallet = economy.get_balance(user_id, "wallet")
                if wallet > 0:
                    stolen = int(wallet * effect.rob_percent)
                    if stolen > 0:
                        economy.update_balance(user_id, -stolen, "wallet")
                        economy.update_balance(bot_id, stolen, "wallet")
                        percent_display = int(effect.rob_percent * 100)
                        logger.info(f"{logger_prefix} Robbed ${stolen} ({percent_display}%) from {message.author}")
                        final_message = f"💰 *Stole ${stolen} from your wallet*\n{effect.message}"
        except Exception as e:
            logger.error(f"{logger_prefix} Rob error: {e}")
    
    # Apply Google search effect
    if effect.google_query and user_query:
        try:
            clean_query = user_query.strip()
            encoded_query = urllib.parse.quote_plus(clean_query)
            google_url = f"https://www.google.com/search?q={encoded_query}"
            final_message = f"{effect.message}\n🔍 {google_url}"
        except Exception as e:
            logger.error(f"{logger_prefix} Google link error: {e}")
    
    # Future effects would be applied here:
    # if effect.reactions:
    #     for emoji in effect.reactions:
    #         await message.add_reaction(emoji)
    # if effect.dm_message:
    #     await message.author.send(effect.dm_message)
    
    return final_message


def format_duration(td: timedelta) -> str:
    """Format timedelta for human-readable logging."""
    total_seconds = int(td.total_seconds())
    
    if total_seconds < 60:
        return f"{total_seconds}s"
    elif total_seconds < 3600:
        return f"{total_seconds // 60}m"
    elif total_seconds < 86400:
        return f"{total_seconds // 3600}h"
    else:
        return f"{total_seconds // 86400}d"


async def process_response(response: str, message, user_query: str = None) -> str:
    """
    One-liner to parse and apply effects from a response.
    
    Usage:
        final_message = await process_response(response, message, user_query)
        await message.reply(final_message)
    
    Args:
        response: Response string (may contain effect prefixes)
        message: Discord message object
        user_query: Original user query (for GOOGLE: effect)
    """
    effect = parse_response(response)
    return await apply_effects(effect, message, user_query, logger_prefix="[EFFECT]")
