"""
background_tasks.py — Background tasks and gossip system.

Provides mixin methods for SonarrAI that handle periodic idle chat,
memory cleanup, and gossip generation.
"""

import random
import logging
from datetime import datetime, timezone

import discord
from discord.ext import tasks

from sonarr.responses import GOSSIP_LINES, IDLE_CHAT_LINES, IDLE_PING_MESSAGES
from utils.database import db

logger = logging.getLogger("bot")


class BackgroundTasksMixin:
    """Mixin providing background tasks for SonarrAI."""

    # ================== GOSSIP ==================

    def get_status_gossip(self, target_id: str) -> str:
        """Get a gossip line based on the target's status."""
        return random.choice(GOSSIP_LINES)

    # ================== MEMORY CLEANUP ==================

    @tasks.loop(hours=168)
    async def memory_cleanup_task(self):
        """Periodically clean up stale entries from tracking dictionaries."""
        logger.debug("[Memory] Cleanup task running...")
        now = datetime.now(timezone.utc).timestamp()
        one_hour_ago = now - 3600
        thirty_seconds_ago = now - 30

        ai_before = len(self.user_ai_calls)
        mention_before = len(self.user_mention_times)

        stale_ai_users = [
            uid for uid, times in self.user_ai_calls.items()
            if not times or all(t <= one_hour_ago for t in times)
        ]
        for uid in stale_ai_users:
            del self.user_ai_calls[uid]

        stale_mention_users = [
            uid for uid, times in self.user_mention_times.items()
            if not times or all(t <= thirty_seconds_ago for t in times)
        ]
        for uid in stale_mention_users:
            del self.user_mention_times[uid]

        # Clean up expired misgendering memory (24 hours)
        try:
            db.cleanup_expired_misgendering(hours_back=24)
            logger.debug("[Memory] Misgendering memory cleanup complete")
        except Exception as e:
            logger.error(f"[Memory] Misgendering cleanup error: {e}")

        logger.info(
            f"[Memory] Cleanup: AI {ai_before}→{len(self.user_ai_calls)} (-{len(stale_ai_users)}), "
            f"Mentions {mention_before}→{len(self.user_mention_times)} (-{len(stale_mention_users)})"
        )

    @memory_cleanup_task.before_loop
    async def before_memory_cleanup(self):
        await self.bot.wait_until_ready()

    # ================== IDLE CHAT ==================

    @tasks.loop(minutes=random.randint(15, 45))
    async def idle_chat_task(self):
        """Bot randomly chats in general channel after 2 hours of no user activity."""
        logger.debug("[IdleChat] Task running...")
        try:
            if not self.bot.guilds or self.time_manager.is_sleep_time():
                logger.debug("[IdleChat] No guilds or sleep time, skipping")
                return

            guild = self.bot.guilds[0]
            guild_id = str(guild.id)
            config = self.bot.server_config.get(guild_id, {})
            general_id = config.get("general_channel")

            if not general_id:
                logger.debug("[IdleChat] No general channel configured")
                return

            channel = self.bot.get_channel(general_id)
            if not channel:
                return

            last_chat = self.last_user_chat_time.get(guild_id)
            if last_chat:
                hours_since_chat = (datetime.now(timezone.utc) - last_chat).total_seconds() / 3600
                if hours_since_chat < 2:
                    logger.debug(f"[IdleChat] Only {hours_since_chat:.1f}h since last chat, need 2h")
                    return
            else:
                self.last_user_chat_time[guild_id] = datetime.now(timezone.utc)
                return

            if random.random() > 0.20:
                logger.debug("[IdleChat] Random skip")
                return

            if random.random() < 0.3:
                online_members = [
                    m for m in guild.members
                    if not m.bot and m.status != discord.Status.offline
                ]
                if online_members:
                    target = random.choice(online_members)
                    message = random.choice(IDLE_PING_MESSAGES).format(target=target)
                    logger.info(f"[IdleChat] Pinging {target.display_name}")
                    await channel.send(message)
            else:
                idle_msg = random.choice(IDLE_CHAT_LINES)
                logger.info(f"[IdleChat] Sending: '{idle_msg}'")
                await channel.send(idle_msg)

        except Exception as e:
            logger.error(f"[IdleChat] Error: {e}")

    @idle_chat_task.before_loop
    async def before_idle_chat(self):
        await self.bot.wait_until_ready()
