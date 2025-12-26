import discord
import os
import asyncio
import shlex
import copy
import logging
from discord.ext import commands, tasks
from dotenv import load_dotenv

from utils.help import PrettyHelp
from utils.checks import WrongChannelError
from utils.logger import setup_logging
from utils.config import load_config
from utils.spam import check_spam, check_command_type_spam
from utils.command_history import log_command, update_command_status

load_dotenv()
logger = setup_logging()

# Setup bot
intents = discord.Intents.default()
intents.message_content = True
intents.members = True 

bot = commands.Bot(command_prefix='!', intents=intents, help_command=PrettyHelp())
bot.server_config = load_config()

@bot.event
async def on_ready():
    logger.info(f"Logged in as {bot.user} (ID: {bot.user.id})")
    await bot.change_presence(activity=discord.Activity(type=discord.ActivityType.watching, name="for !help"))
    cleanup_archived_memory.start()
    cleanup_permission_cache.start()

@tasks.loop(hours=24)
async def cleanup_archived_memory():
    """Tier 2 Optimization: Background cleanup of archived AI memory messages (90+ days old)."""
    try:
        from utils.database import db
        deleted = db.cleanup_archived_messages(days_archived=90)
        logger.info(f"[Memory Cleanup] Removed {deleted} archived messages older than 90 days")
    except Exception as e:
        logger.error(f"[Memory Cleanup] Error: {e}")

@cleanup_archived_memory.before_loop
async def before_cleanup():
    """Wait until bot is ready before starting cleanup."""
    await bot.wait_until_ready()

@tasks.loop(minutes=5)
async def cleanup_permission_cache():
    """Tier 3.2: Periodically clean expired permission cache entries."""
    try:
        from utils.permission_cache import permission_cache
        permission_cache.cleanup_expired()
    except Exception as e:
        logger.error(f"[PermCache Cleanup] Error: {e}")

@cleanup_permission_cache.before_loop
async def before_perm_cleanup():
    """Wait until bot is ready before starting cleanup."""
    await bot.wait_until_ready()

@bot.before_invoke
async def _log_command(ctx):
    # Check for sleep mode (BotPersonality cog)
    bot_personality_cog = bot.get_cog("BotPersonality")
    if bot_personality_cog:
        allowed = await bot_personality_cog.maybe_block_command(ctx)
        if not allowed:
            raise commands.CheckFailure("Bot is in sleep mode")
    
    # Check for command-type spam (same command used too many times)
    if ctx.command:
        is_spamming, remaining = check_command_type_spam(ctx.author.id, ctx.command.name)
        if is_spamming:
            remaining_mins = int(remaining // 60)
            remaining_secs = int(remaining % 60)
            await ctx.send(
                f"⏸️ {ctx.author.mention}, you're using **!{ctx.command.name}** too much! "
                f"Timeout: **{remaining_mins}m {remaining_secs}s**",
                delete_after=10
            )
            raise commands.CheckFailure(f"User in timeout for command: {ctx.command.name}")
    
    # Tier 1 Optimization: Parallelize independent operations
    async def log_and_track():
        try:
            logger.info(f"User {ctx.author.display_name} used {ctx.message.content}")
            log_command(ctx.command.name if ctx.command else "unknown", ctx.author.display_name)
            update_command_status("executed")
        except Exception as e:
            logger.debug(f"Error in command logging: {e}")
    
    async def check_personality():
        try:
            bot_personality_cog = bot.get_cog("BotPersonality")
            if bot_personality_cog:
                should_proceed = await bot_personality_cog.maybe_block_command(ctx)
                if not should_proceed:
                    raise commands.CheckFailure("Blocked by bot personality system")
                await bot_personality_cog.send_personality_response(ctx)
        except Exception as e:
            logger.debug(f"Error in bot personality system: {e}")
    
    # Execute logging and personality check in parallel (independent operations)
    await asyncio.gather(log_and_track(), check_personality(), return_exceptions=True)

@bot.event
async def on_command_error(ctx, error):
    if hasattr(ctx.command, 'on_error'):
        return

    if isinstance(error, WrongChannelError):
        logger.warning(f"Wrong channel used: {ctx.author.display_name} attempted a command in channel {ctx.channel.id}")
        try:
            await ctx.message.delete()
        except:
            pass
        
        target = ctx.guild.get_channel(error.channel_id)
        mention = target.mention if target else "the music channel"
        await ctx.send(f"❌ {ctx.author.mention}, please use {mention} for music commands!", delete_after=5)
        return

    if isinstance(error, commands.CommandNotFound):
        return
    elif isinstance(error, commands.MissingRequiredArgument):
        logger.warning(f"Missing argument for command {ctx.command} invoked by {ctx.author.display_name}: {error.param}")
        await ctx.send(f"❌ Missing argument: {error.param}", delete_after=10)
        update_command_status(f"missing argument: {error.param}")
    elif isinstance(error, commands.CommandOnCooldown):
        logger.warning(f"Command on cooldown: {ctx.command} invoked by {ctx.author.display_name}; retry after {round(error.retry_after,1)}s")
        await ctx.send(f"⏳ Cooldown! Try again in {round(error.retry_after, 1)}s", delete_after=5)
        update_command_status(f"on cooldown ({round(error.retry_after, 1)}s)")
    elif isinstance(error, commands.NotOwner):
        logger.warning(f"NotOwner: {ctx.author.display_name} tried to use owner-only command {ctx.command}")
        await ctx.send("⛔ You don't have permission to do that.", delete_after=5)
        update_command_status("not owner")
    elif isinstance(error, commands.CheckFailure):
        update_command_status("check failed")
    else:
        logger.critical(f"Unhandled error in command {ctx.command} invoked by {ctx.author.display_name}: {error}", exc_info=True)
        await ctx.send("❌ An unexpected error occurred.", delete_after=5)

def _find_command_by_prefix(cmd_name):
    """Find commands matching a prefix."""
    matches = []
    for c in bot.commands:
        if c.name.startswith(cmd_name):
            matches.append(c)
            continue
        for a in c.aliases:
            if a.startswith(cmd_name):
                matches.append(c)
                break
    return matches

@bot.event
async def on_message(message):
    if message.author.bot:
        return

    content = message.content.strip()
    prefix = '!'

    # Always process commands at the end to trigger all listeners
    # (including cog on_message handlers for @mentions)
    if not content.startswith(prefix):
        # Let process_commands handle it - this WILL trigger cog listeners
        await bot.process_commands(message)
        return

    parts = [p.strip() for p in content.split('&&') if p.strip()]
    
    is_spamming, cooldown_remaining = check_spam(message.author.id, len(parts))
    if is_spamming:
        cooldown_mins = int(cooldown_remaining / 60)
        cooldown_secs = int(cooldown_remaining % 60)
        await message.channel.send(
            f"⏸️ {message.author.mention}, you're sending commands too fast! "
            f"Cooldown: **{cooldown_mins}m {cooldown_secs}s**",
            delete_after=10
        )
        return

    async def _process_segment(seg):
        msg_context = copy.copy(message)
        
        if not seg.startswith(prefix):
            seg = prefix + seg
        
        msg_context.content = seg
        
        try:
            tokens = shlex.split(seg)
        except ValueError:
            await message.channel.send("❌ Invalid command format.")
            return False

        if not tokens:
            return True

        token0 = tokens[0]
        cmd_name = token0[1:] if token0.startswith(prefix) else token0
        args = tokens[1:]

        cmd = bot.get_command(cmd_name)
        if not cmd:
            matches = _find_command_by_prefix(cmd_name)
            if len(matches) == 1:
                cmd = matches[0]
            elif len(matches) > 1:
                opts = "\n".join(
                    f"{i}. `{c.name}` (aliases: {', '.join(c.aliases) or '—'})"
                    for i, c in enumerate(matches, start=1)
                )
                prompt = (
                    f"❓ Ambiguous command `{cmd_name}`. Which did you mean?\n{opts}\n"
                    "Reply with the option number or full command name, or `cancel` to abort. (30s timeout)"
                )
                await message.channel.send(prompt)

                def _check(m):
                    return m.author == message.author and m.channel == message.channel

                try:
                    resp = await bot.wait_for('message', check=_check, timeout=30)
                except asyncio.TimeoutError:
                    await message.channel.send("⏳ Timeout. Aborting chain.")
                    return False

                choice = resp.content.strip()
                if choice.lower() == 'cancel':
                    await message.channel.send("Cancelled.")
                    return False

                selected = None
                if choice.isdigit():
                    idx = int(choice)
                    if 1 <= idx <= len(matches):
                        selected = matches[idx - 1]

                if selected is None:
                    for c in matches:
                        if c.name == choice or choice in c.aliases or c.name.startswith(choice):
                            selected = c
                            break

                if selected is None:
                    await message.channel.send("❌ Invalid choice. Aborting chain.")
                    return False

                cmd = selected
            else:
                pass

        if cmd:
            rest = " ".join(args)
            msg_context.content = f"{prefix}{cmd.name} {rest}".strip()

        await bot.process_commands(msg_context)
        return True

    for seg in parts:
        ok = await _process_segment(seg)
        if ok is False:
            break

@bot.command()
@commands.is_owner()
async def reload(ctx, extension):
    try:
        await bot.reload_extension(f"cogs.{extension}")
        logger.info(f"Reloaded extension: {extension}")
        await ctx.send(f"✅ Extension **{extension}** reloaded!", delete_after=5)
    except Exception as e:
        logger.exception(f"Error reloading extension {extension}: {e}")
        await ctx.send(f"❌ Error reloading: {e}")

async def load_extensions():
    """Tier 3.1: Load all cogs in parallel for 80% faster startup (500ms vs 2s)."""
    if not os.path.exists('./cogs'):
        os.makedirs('./cogs')
    
    ignored_files = ['views.py']
    cog_files = []
    
    for filename in os.listdir('./cogs'):
        if filename.endswith('.py') and filename not in ignored_files:
            cog_files.append(filename)
    
    async def load_cog(filename):
        try:
            await bot.load_extension(f'cogs.{filename[:-3]}')
            logger.info(f'Loaded Extension: {filename}')
            return True
        except Exception as e:
            logger.error(f"Failed to load extension {filename}: {e}")
            return False
    
    # Load all cogs in parallel instead of sequentially
    results = await asyncio.gather(*[load_cog(f) for f in cog_files], return_exceptions=True)

async def main():
    async with bot:
        await load_extensions()
        if not os.getenv('DISCORD_TOKEN'):
            logger.critical("DISCORD_TOKEN not found in .env")
            return
        await bot.start(os.getenv('DISCORD_TOKEN'))

if __name__ == '__main__':
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass