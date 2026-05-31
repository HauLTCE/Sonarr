import discord
import os

# Force math libraries and PyTorch to utilize 10 threads (leaving 2 for OS)
os.environ["OMP_NUM_THREADS"] = "10"
os.environ["MKL_NUM_THREADS"] = "10"
os.environ["OPENBLAS_NUM_THREADS"] = "10"

import asyncio
import shlex
import copy
import wavelink
from discord.ext import commands, tasks
from dotenv import load_dotenv

from utils.help import PrettyHelp
from utils.checks import WrongChannelError, BotRestrictedTimeError
from core.logger import setup_logging
from utils.config import load_config
from utils.spam import check_spam, check_command_type_spam
from utils.command_history import log_command, update_command_status

load_dotenv()
logger = setup_logging()

intents = discord.Intents.default()
intents.message_content = True
intents.members = True 

bot = commands.Bot(
    command_prefix='!',
    intents=intents,
    help_command=PrettyHelp(),
    allowed_mentions=discord.AllowedMentions(everyone=False, roles=False),
)
bot.server_config = load_config()
bot.lavalink_ready = False


async def connect_lavalink(max_attempts: int = 8, delay_seconds: int = 3) -> bool:
    uri = os.getenv("LAVALINK_URI", "http://127.0.0.1:2333")
    password = os.getenv("LAVALINK_PASSWORD", "youshallnotpass")

    for attempt in range(1, max_attempts + 1):
        try:
            connected_nodes = [
                node for node in wavelink.Pool.nodes.values()
                if node.status is wavelink.NodeStatus.CONNECTED
            ]

            if connected_nodes:
                bot.lavalink_ready = True
                return True

            if wavelink.Pool.nodes:
                await wavelink.Pool.close()

            node = wavelink.Node(uri=uri, password=password, retries=2)
            await wavelink.Pool.connect(nodes=[node], client=bot, cache_capacity=100)

            bot.lavalink_ready = True
            logger.info("Connected to Lavalink at %s", uri)
            return True
        except Exception as e:
            logger.warning(
                "Lavalink connect attempt %s/%s failed: %s",
                attempt,
                max_attempts,
                e,
            )
            await asyncio.sleep(delay_seconds)

    bot.lavalink_ready = False
    logger.error("Unable to connect to Lavalink after %s attempts", max_attempts)
    return False

@bot.event
async def on_ready():
    if not bot.lavalink_ready:
        await connect_lavalink()

    logger.info(f"Logged in as {bot.user} (ID: {bot.user.id})")
    await bot.change_presence(activity=discord.Activity(type=discord.ActivityType.watching, name="for !help"))

    if not cleanup_message_cache.is_running():
        cleanup_message_cache.start()

@tasks.loop(hours=24)
async def cleanup_message_cache():
    """Smart LRU cleanup of message classification cache daily per guild."""
    try:
        from utils.database import db
        db.cleanup_all_guilds_cache()
    except Exception as e:
        logger.error(f"[Cache Cleanup] Message cache error: {e}")
    try:
        from utils.spam import cleanup_spam_data
        cleanup_spam_data()
    except Exception as e:
        logger.error(f"[Cache Cleanup] Spam data error: {e}")

@cleanup_message_cache.before_loop
async def before_message_cache_cleanup():
    await bot.wait_until_ready()

@bot.before_invoke
async def _log_command(ctx):
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
    
    async def log_and_track():
        try:
            logger.info(f"User {ctx.author.display_name} used {ctx.message.content}")
            log_command(ctx.command.name if ctx.command else "unknown", ctx.author.display_name)
            update_command_status("executed")
        except Exception as e:
            pass
    
    await log_and_track()

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
    
    if isinstance(error, BotRestrictedTimeError):
        logger.info(f"[RestrictedTime] {ctx.author.display_name} tried {ctx.command} during restricted hours")
        await ctx.send(f"😴 {error.reason}", delete_after=10)
        update_command_status("restricted time")
        return

    if isinstance(error, commands.CommandNotFound):
        # Fuzzy "did you mean?" suggestions
        invoked = ctx.invoked_with.lower()
        if not invoked or len(invoked) < 2:
            return
        
        suggestion = _fuzzy_suggest(invoked)
        if suggestion:
            await ctx.send(
                f"That's not a command. Did you mean **`!{suggestion}`**?",
                delete_after=8
            )
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


def _fuzzy_suggest(invoked: str) -> str | None:
    """Find the closest matching command name using fuzzy matching."""
    import difflib
    
    # Build a list of all command names + aliases
    all_names = []
    for cmd in bot.commands:
        all_names.append(cmd.name)
        all_names.extend(cmd.aliases)
        # Include subcommands for groups
        if isinstance(cmd, commands.Group):
            for sub in cmd.commands:
                all_names.append(f"{cmd.name} {sub.name}")
                all_names.extend(f"{cmd.name} {a}" for a in sub.aliases)
    
    # Also add help-related keywords
    all_names.extend(["help", "help games", "help dungeon", "help economy", "help music",
                       "help adventure", "dungeon_guide", "dguide", "advguide"])
    
    matches = difflib.get_close_matches(invoked, all_names, n=1, cutoff=0.6)
    if matches:
        return matches[0]
    
    # Fallback: prefix match
    for name in all_names:
        if name.startswith(invoked) or invoked.startswith(name):
            return name

    return None


def _find_command_by_prefix(cmd_name: str) -> list:
    """Return commands whose name or any alias starts with cmd_name.

    Used by the `&&` command-chain handler to resolve partial command names.
    Exact name/alias matches short-circuit to a single result so a full name
    never reads as ambiguous against longer commands sharing its prefix.
    """
    cmd_name = cmd_name.lower()
    if not cmd_name:
        return []

    # Exact match wins outright.
    exact = bot.get_command(cmd_name)
    if exact:
        return [exact]

    matches = []
    for cmd in bot.commands:
        names = [cmd.name] + list(cmd.aliases)
        if any(n.lower().startswith(cmd_name) for n in names):
            matches.append(cmd)
    return matches


@bot.event
async def on_message(message):
    if message.author.bot:
        return

    content = message.content.strip()
    prefix = '!'

    if not content.startswith(prefix):
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
    """Load all cogs - supports both file cogs and package cogs."""
    if not os.path.exists('./cogs'):
        os.makedirs('./cogs')
    
    # Package-based cogs (directories with __init__.py)
    package_cogs = ['sonarr_ai', 'music', 'games', 'adventure']
    
    # File-based cogs to ignore (old files superseded by packages, or view-only files)
    ignored_files = ['views.py', 'sonarr_ai.py', 'music.py']
    
    cog_names = []
    
    # Add package cogs
    for pkg in package_cogs:
        pkg_path = os.path.join('./cogs', pkg)
        if os.path.isdir(pkg_path) and os.path.exists(os.path.join(pkg_path, '__init__.py')):
            cog_names.append(pkg)
    
    # Add file-based cogs
    for filename in os.listdir('./cogs'):
        if filename.endswith('.py') and filename not in ignored_files:
            cog_names.append(filename[:-3])
    
    async def load_cog(name):
        try:
            await bot.load_extension(f'cogs.{name}')
            logger.info(f'Loaded Extension: {name}')
            return True
        except Exception as e:
            logger.error(f"Failed to load extension {name}: {e}")
            return False
    
    results = await asyncio.gather(*[load_cog(n) for n in cog_names], return_exceptions=True)

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
