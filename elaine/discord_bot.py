"""Discord integration — a thin, text-only adapter (PERSISTENCE §7, Plan Phase 11).

on_message -> store.load -> AffectEngine.step -> channel.send -> store.save, keyed per
(guild, user). This is the CLI loop with Discord I/O swapped in; the core engine is
unchanged.

TEXT-ONLY, NO MODERATION. Elaine never kicks/bans/mutes — this adapter touches only
message reading and channel.send. (Decision 2026-06-30: moderation dropped entirely.)
The response path makes zero privileged Discord API calls.

discord.py is an OPTIONAL dependency: the message-handling logic (handle_message) is a
pure function testable with a fake message, so CI needs no live network and no library.
"""
from __future__ import annotations

import logging
import os
from dataclasses import dataclass

from .affect import Personality
from .script import LoadedScript, load
from .self_model import AffectEngine, GlobalState
from .store import Store

logger = logging.getLogger(__name__)

# Privileged actions this adapter must NEVER call (asserted by tests).
FORBIDDEN_ACTIONS = ("kick", "ban", "timeout", "edit_roles", "move_to", "mute", "deafen")


@dataclass
class IncomingMessage:
    """The minimal shape we read from a Discord message (so tests can fake it)."""

    guild_id: str
    author_id: str
    author_is_bot: bool
    content: str


def scope_key(guild_id: str, author_id: str) -> str:
    return f"{guild_id}:{author_id}"


class ElaineBot:
    """Framework-agnostic core. Wrap with discord.py in run_live()."""

    def __init__(self, script_path: str, db_path: str = "elaine.db"):
        self.loaded: LoadedScript = load(script_path)
        self.personality = Personality.from_config(self.loaded.personality)
        self.store = Store(db_path, personality=self.personality,
                           start_state=self.loaded.start)

    def handle_message(self, msg: IncomingMessage) -> str | None:
        """Process one message; return the reply text (None if ignored). Pure + testable."""
        if msg.author_is_bot:
            logger.debug("handle: ignoring bot author %s", msg.author_id)
            return None
        content = (msg.content or "").strip()
        if not content:
            logger.debug("handle: ignoring empty message from %s:%s",
                         msg.guild_id, msg.author_id)
            return None  # empty / attachment-only

        key = scope_key(msg.guild_id, msg.author_id)
        lock = self.store.lock_for(key)
        with lock:
            loaded_self = self.store.load(key, guild_id=msg.guild_id)
            if logger.isEnabledFor(logging.DEBUG):
                ss = loaded_self.self_state
                logger.debug(
                    "handle: %s loaded state=%s clock=%d role=%s anger=%.1f room_mood=%.2f",
                    key, ss.dialogue_state, loaded_self.logical_clock,
                    ss.registers.role, ss.registers.anger, loaded_self.global_mood,
                )
            # Share the per-guild room mood so it actually contributes to mode
            # selection (it was always 0 before — the engine got a fresh, empty
            # GlobalState and save hardcoded mood to 0.0).
            global_state = GlobalState(global_mood=loaded_self.global_mood)
            ae = AffectEngine(self.loaded, personality=self.personality,
                              self_state=loaded_self.self_state, global_state=global_state)
            # restore slots into the engine's memory, then reattach their TTLs
            for k, v in loaded_self.slots.items():
                ae.memory.set(k, v)
            ae.memory.apply_expiry(loaded_self.slot_expiry)
            ae.memory.turn = loaded_self.logical_clock

            try:
                result = ae.step(content)
            except Exception:
                # Log with the scope + input that triggered it, then re-raise so the
                # caller still decides what to do (the Discord cog degrades gracefully).
                logger.exception("handle: %s engine.step crashed on %r", key, content)
                raise

            self.store.save(key, ae.self_state, ae.memory.slots(),
                            logical_clock=ae.memory.turn, guild_id=msg.guild_id,
                            slot_expiry=ae.memory.slot_expiry(),
                            global_mood=ae.global_state.global_mood)
            logger.debug("handle: %s saved state=%s clock=%d", key,
                         ae.self_state.dialogue_state, ae.memory.turn)
        return result.reply or None


def run_live(script_path: str, db_path: str = "elaine.db") -> None:  # pragma: no cover
    """Wire ElaineBot into discord.py. Requires the optional `discord` dependency.

    Token from env (DISCORD_TOKEN), never committed.
    """
    try:
        import discord
    except ImportError as e:
        raise SystemExit("discord.py not installed: pip install 'elaine[discord]'") from e

    token = os.environ.get("DISCORD_TOKEN")
    if not token:
        raise SystemExit("set DISCORD_TOKEN in the environment (never commit it)")

    bot = ElaineBot(script_path, db_path)
    intents = discord.Intents.default()
    intents.message_content = True
    client = discord.Client(intents=intents)

    @client.event
    async def on_message(message):
        if message.author == client.user:
            return
        incoming = IncomingMessage(
            guild_id=str(message.guild.id) if message.guild else f"dm",
            author_id=str(message.author.id),
            author_is_bot=message.author.bot,
            content=message.content,
        )
        reply = bot.handle_message(incoming)
        if reply:
            await message.channel.send(reply)  # the ONLY Discord write — text only

    logger.info("run_live: starting E.L.A.I.N.E (script=%s, db=%s)", script_path, db_path)
    client.run(token)
