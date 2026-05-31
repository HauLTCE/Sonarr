"""Shared helpers for the dungeon: a ctx-like stub, a text→interaction adapter,
and the log-channel notifier. Kept separate so views.py and __init__.py can both
import them without a circular dependency."""
import logging
from utils.config import get_guild_config

log = logging.getLogger("bot")


class SimpleContext:
    """Lightweight ctx-like object for interaction-driven flows."""
    def __init__(self, user):
        self.author = user


class _TextResponse:
    """Stands in for discord.Interaction.response on the text-command path."""
    def __init__(self, parent):
        self._p = parent

    async def defer(self):
        self._p._deferred = True

    async def send_message(self, content=None, *, embed=None, ephemeral=False, **kw):
        # Text commands have no ephemeral channel; auto-delete to keep things tidy.
        await self._p.channel.send(
            content=content, embed=embed,
            delete_after=8 if ephemeral else None
        )

    async def edit_message(self, **kwargs):
        await self._p.edit_original_response(**kwargs)


class TextInteraction:
    """Adapts a commands.Context to the slice of discord.Interaction that
    CombatView/AdventureView use, so !attack/!flee/!explore reuse button logic.

    The fix vs. the old adapter: edits happen on a tracked *screen message*
    (the bot message that already holds the embed) instead of sending a new
    message every turn. Reading interaction.message.embeds[0] therefore returns
    the live embed instead of a blank stub.
    """
    def __init__(self, cog, ctx, screen_message):
        self.cog = cog
        self.ctx = ctx
        self.user = ctx.author
        self.guild = ctx.guild
        self.channel = ctx.channel
        self.message = screen_message  # real discord.Message holding the embed
        self._deferred = False
        self.response = _TextResponse(self)

    async def edit_original_response(self, **kwargs):
        if self.message is not None:
            try:
                await self.message.edit(**kwargs)
                return self.message
            except Exception:
                pass
        msg = await self.channel.send(**kwargs)
        self.message = msg
        return msg

    async def original_response(self):
        return self.message


async def send_to_log_channel(bot, guild, message: str):
    """Post to the configured logs channel, if any. Silent on misconfig."""
    if not guild:
        return
    ch_id = get_guild_config(guild.id, "logs_channel")
    if not ch_id:
        return
    ch = bot.get_channel(int(ch_id))
    if not ch:
        log.warning(f"[LogChannel] Channel {ch_id} not found in guild {guild.name}")
        return
    try:
        await ch.send(message)
    except Exception as e:
        log.warning(f"[LogChannel] Failed to send: {e}")
