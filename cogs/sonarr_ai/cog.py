"""
cog.py — Sonarr's chat personality, powered by the E.L.A.I.N.E engine.

E.L.A.I.N.E is a stateful, text-in / text-out conversational AI built as a symbolic
Extended Finite State Machine. No LLM, ever — every reply is deterministic and traces
to one (state, transition) pair, drawing on the ported sonarr response pools.

Flow: trigger gate (mention or reply-to-bot only) -> clean mentions -> ElaineBot.step
(per-(guild, user) state persisted in SQLite) -> send the text reply.

TEXT-ONLY, NO MODERATION. This adapter never kicks/bans/mutes/renames — it only reads
messages and sends text, exactly per E.L.A.I.N.E's design.
"""
import re
import asyncio
import logging
from pathlib import Path

import discord
from discord.ext import commands

import elaine
from elaine.discord_bot import ElaineBot, IncomingMessage

logger = logging.getLogger("bot")

# The personality script + its included pools live in elaine/persona/ (kept out of any
# dir named 'scripts'/'data' so deploy.py uploads them). Per-user affect/dialogue state
# persists in data/elaine.db (added to deploy.py's PRESERVE_DATA so it survives redeploys).
_ELAINE_PKG = Path(elaine.__file__).resolve().parent
_BOT_ROOT = _ELAINE_PKG.parent
SCRIPT_PATH = _ELAINE_PKG / "persona" / "elaine.yaml"
DB_PATH = _BOT_ROOT / "data" / "elaine.db"


class SonarrAI(commands.Cog):
    """Sonarr's rude chat personality, running on the E.L.A.I.N.E symbolic FSM."""

    def __init__(self, bot):
        self.bot = bot
        DB_PATH.parent.mkdir(parents=True, exist_ok=True)
        # Loads + statically validates the YAML fail-fast; raises if the script is broken.
        self.engine = ElaineBot(str(SCRIPT_PATH), db_path=str(DB_PATH))
        logger.info(f"[SonarrAI] E.L.A.I.N.E engine ready (script={SCRIPT_PATH.name}, db={DB_PATH.name})")

    def _clean_mentions(self, content: str) -> str:
        """Strip the bot's own mention, replace other user mentions with 'someone'."""
        if self.bot.user:
            content = content.replace(f"<@{self.bot.user.id}>", "").replace(f"<@!{self.bot.user.id}>", "")
        content = re.sub(r"<@!?\d+>", "someone", content)
        content = re.sub(r"<@&\d+>", "", content)
        content = re.sub(r"<#\d+>", "", content)
        return re.sub(r"\s+", " ", content).strip()

    @commands.Cog.listener()
    async def on_message(self, message):
        if message.author.bot or not message.guild:
            return

        # Trigger gate: only respond to a direct mention or a reply to one of our messages.
        ref = message.reference
        replied_to_bot = (
            ref is not None
            and isinstance(getattr(ref, "resolved", None), discord.Message)
            and ref.resolved.author == self.bot.user
        )
        if not ((self.bot.user in message.mentions) or replied_to_bot):
            return
        # Leave command invocations to the command processor.
        if message.content.startswith("!"):
            return

        # A bare ping with no words still deserves a reply — feed her a neutral opener
        # so the FSM produces something in-character instead of silently ignoring it.
        text = self._clean_mentions(message.content) or "hey"

        incoming = IncomingMessage(
            guild_id=str(message.guild.id),
            author_id=str(message.author.id),
            author_is_bot=False,
            content=text,
        )

        # handle_message is synchronous (SQLite + a per-user lock); run it off the loop.
        async with message.channel.typing():
            reply = await asyncio.to_thread(self.engine.handle_message, incoming)

        if reply and reply.strip():
            await message.reply(reply, mention_author=False)


async def setup(bot):
    await bot.add_cog(SonarrAI(bot))
