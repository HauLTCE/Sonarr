"""
Response Effects Handler
Parses special prefixes in responses and applies effects like timeouts, robbery, etc.

Supported prefixes:
- TIMEOUT:message - Apply 1 hour timeout
- TIMEOUT:30m:message - Apply custom duration timeout
- ROB:message - Rob 5% from user's wallet
- ROB:10:message - Rob custom % from user's wallet
- SEARCH:GOOGLE:message - Add Google search link
- SEARCH:YOUTUBE:message - Add YouTube search link
- SEARCH:WIKIPEDIA:message - Add Wikipedia search link
- SEARCH:CHATGPT:message - Add ChatGPT link
- SEARCH:REDDIT:message - Add Reddit search link
- RENAME:NewNick:message - Change user's nickname (Discord shows system message)
- REACT:🤡:message - Add emoji reaction to user's message
- REACT:🤡 - Add reaction without bot reply
- DOUBLE:first message||second message - Send two messages with a delay
- (Legacy: GOOGLE:message still supported for backwards compatibility)
- (More can be added later: DM:, RECURSIVE:, etc.)
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
    search_platform: Optional[str] = None  # Platform to search: google, youtube, wikipedia, chatgpt, reddit
    search_query: Optional[str] = None  # User's query to search
    rename_nickname: Optional[str] = None  # New nickname to set
    reactions: List[str] = field(default_factory=list)  # Emoji reactions to add
    double_message: Optional[str] = None  # Second message to send after a delay
    # Future effects can be added here:
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
        "RENAME:Clown:You're dressed for it" -> ResponseEffect(message="You're...", rename_nickname="Clown")
        "REACT:🤡:Honk honk" -> ResponseEffect(message="Honk honk", reactions=["🤡"])
        "REACT:🤡" -> ResponseEffect(message="", reactions=["🤡"])
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
    
    # Parse SEARCH: prefix (SEARCH:GOOGLE:, SEARCH:YOUTUBE:, etc.)
    if remaining.startswith("SEARCH:"):
        remaining = remaining[7:]  # Remove "SEARCH:"
        platform_match = re.match(r'^(GOOGLE|YOUTUBE|WIKIPEDIA|CHATGPT|REDDIT):(.+)$', remaining, re.DOTALL)
        
        if platform_match:
            effect.search_platform = platform_match.group(1).lower()
            remaining = platform_match.group(2)
        else:
            # Fallback to google if no platform specified
            effect.search_platform = "google"
    
    # Legacy GOOGLE: prefix support (backwards compatibility)
    if remaining.startswith("GOOGLE:"):
        remaining = remaining[7:]
        effect.search_platform = "google"
    
    # Parse RENAME: prefix
    if remaining.startswith("RENAME:"):
        remaining = remaining[7:]
        nickname_match = re.match(r'^([^:]+):(.+)$', remaining, re.DOTALL)
        
        if nickname_match:
            effect.rename_nickname = nickname_match.group(1).strip()
            remaining = nickname_match.group(2)
        else:
            # RENAME without message is allowed
            effect.rename_nickname = remaining.strip()
            remaining = ""
    
    # Parse REACT: prefix
    if remaining.startswith("REACT:"):
        remaining = remaining[6:]
        emoji_match = re.match(r'^([^:]+):(.+)$', remaining, re.DOTALL)
        
        if emoji_match:
            effect.reactions.append(emoji_match.group(1).strip())
            remaining = emoji_match.group(2)
        else:
            # REACT without message (reaction-only)
            effect.reactions.append(remaining.strip())
            remaining = ""
    
    # Parse DOUBLE: prefix (send two messages)
    if remaining.startswith("DOUBLE:"):
        remaining = remaining[7:]
        double_match = re.match(r'^(.+?)\|\|(.+)$', remaining, re.DOTALL)
        
        if double_match:
            remaining = double_match.group(1).strip()
            effect.double_message = double_match.group(2).strip()
    
    effect.message = remaining
    return effect


async def apply_effects(effect: ResponseEffect, message, user_query: str = None, logger_prefix: str = "[EFFECT]") -> tuple[str, str | None]:
    """
    Apply all effects from a parsed response to a Discord message.
    
    Args:
        effect: Parsed ResponseEffect object
        message: Discord message object (for author, channel, etc.)
        user_query: Original user query (for GOOGLE: effect)
        logger_prefix: Prefix for log messages
    
    Returns:
        Tuple of (main_message, followup_message). followup_message is None if no DOUBLE: effect.
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
    
    # Apply search effect (Google, YouTube, Wikipedia, ChatGPT, Reddit)
    if effect.search_platform and user_query:
        try:
            clean_query = user_query.strip()
            encoded_query = urllib.parse.quote_plus(clean_query)
            
            # Generate platform-specific URL
            if effect.search_platform == "google":
                search_url = f"https://www.google.com/search?q={encoded_query}"
                emoji = "🔍"
            elif effect.search_platform == "youtube":
                search_url = f"https://www.youtube.com/results?search_query={encoded_query}"
                emoji = "📺"
            elif effect.search_platform == "wikipedia":
                search_url = f"https://en.wikipedia.org/wiki/Special:Search?search={encoded_query}"
                emoji = "📖"
            elif effect.search_platform == "chatgpt":
                # ChatGPT doesn't have direct search URL, use Google to find ChatGPT + query
                search_url = f"https://chat.openai.com/"
                emoji = "🤖"
            elif effect.search_platform == "reddit":
                search_url = f"https://www.reddit.com/search/?q={encoded_query}"
                emoji = "🗨️"
            else:
                search_url = f"https://www.google.com/search?q={encoded_query}"
                emoji = "🔍"
            
            final_message = f"{effect.message}\n{emoji} {search_url}"
            logger.info(f"{logger_prefix} Added {effect.search_platform} search link")
        except Exception as e:
            logger.error(f"{logger_prefix} Search link error: {e}")
    
    # Apply rename effect
    if effect.rename_nickname:
        try:
            old_nick = message.author.display_name
            await message.author.edit(nick=effect.rename_nickname, reason="Bot penalty")
            logger.warning(f"{logger_prefix} Renamed {old_nick} to '{effect.rename_nickname}'")
            # Discord automatically shows system message: "Bot changed User to NewNick"
        except discord.Forbidden:
            logger.warning(f"{logger_prefix} Could not rename {message.author} - missing permissions")
        except discord.HTTPException as e:
            logger.error(f"{logger_prefix} Rename error: {e}")
        except Exception as e:
            logger.error(f"{logger_prefix} Unexpected rename error: {e}")
    
    # Apply reaction effects
    if effect.reactions:
        for emoji in effect.reactions:
            try:
                await message.add_reaction(emoji)
                logger.info(f"{logger_prefix} Added reaction {emoji} to message")
            except discord.Forbidden:
                logger.warning(f"{logger_prefix} Could not add reaction {emoji} - missing permissions")
            except discord.HTTPException as e:
                logger.warning(f"{logger_prefix} Invalid emoji {emoji}: {e}")
            except Exception as e:
                logger.error(f"{logger_prefix} Unexpected reaction error: {e}")
    
    # Future effects would be applied here:
    # if effect.dm_message:
    #     await message.author.send(effect.dm_message)
    
    # Return main message and optional followup for DOUBLE: effect
    main_msg = final_message if final_message else None
    return (main_msg, effect.double_message)


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


async def process_response(response: str, message, user_query: str = None) -> tuple[str, str | None]:
    """
    One-liner to parse and apply effects from a response.
    
    Usage:
        main_msg, followup = await process_response(response, message, user_query)
        await message.reply(main_msg)
        if followup:
            await asyncio.sleep(1.5)
            await message.channel.send(followup)
    
    Args:
        response: Response string (may contain effect prefixes)
        message: Discord message object
        user_query: Original user query (for GOOGLE: effect)
    
    Returns:
        Tuple of (main_message, followup_message). followup_message is None if no DOUBLE: effect.
    """
    effect = parse_response(response)
    return await apply_effects(effect, message, user_query, logger_prefix="[EFFECT]")


async def send_response_with_effects(response: str, message, user_query: str = None, delay: float = 1.5) -> bool:
    """
    Process and send a response, automatically handling DOUBLE: effect.
    
    Usage:
        await send_response_with_effects(response, message, user_query)
    
    Args:
        response: Response string (may contain effect prefixes)
        message: Discord message object
        user_query: Original user query (for SEARCH: effects)
        delay: Seconds to wait before sending followup (default 1.5s)
    
    Returns:
        True if a message was sent, False otherwise
    """
    import asyncio
    
    main_msg, followup = await process_response(response, message, user_query)
    
    if main_msg is not None and main_msg.strip():
        await message.reply(main_msg, mention_author=False)
        
        if followup:
            await asyncio.sleep(delay)
            await message.channel.send(followup)
        
        return True
    
    return False
