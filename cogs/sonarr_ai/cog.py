"""
cog.py — Main SonarrAI cog class.

A simplified AI cog that relies purely on NLP classification to generate responses.
Includes sleep mode, user mention cleaning, and full effect support.
"""

import os
import re
import random
import logging
import asyncio
from datetime import datetime, timezone, timedelta

import discord
from discord.ext import commands

from sonarr import MessageClassifier
from sonarr.responses.effects import parse_response, apply_effects
from sonarr.input_check.pipeline import ClassifierMixin
from sonarr.responses.selector import ResponseMixin

logger = logging.getLogger("bot")

# Server timezone (UTC+7)
SERVER_TZ = timezone(timedelta(hours=7))


def get_sleep_state():
    """
    Determine the bot's sleep state based on server time (UTC+7).
    
    Returns one of:
      - "asleep"        (22:00 - 06:00, hard sleep)
      - "evening_grace"  (21:30 - 22:00, probability ramp from 0% to 100%)
      - "morning_grace"  (06:00 - 06:30, probability ramp from 100% to 0%)
      - "awake"          (06:30 - 21:30, fully awake)
    
    For grace periods, also returns a probability (0.0-1.0) of using grace response.
    """
    now = datetime.now(SERVER_TZ)
    hour = now.hour
    minute = now.minute
    time_minutes = hour * 60 + minute  # minutes since midnight

    # Hard sleep: 22:00 (1320) to 06:00 (360)
    if time_minutes >= 1320 or time_minutes < 360:
        return "asleep", 1.0

    # Evening grace: 21:30 (1290) to 22:00 (1320)
    # Probability ramps from 0% at 21:30 to 100% at 22:00
    if 1290 <= time_minutes < 1320:
        progress = (time_minutes - 1290) / 30.0  # 0.0 at 21:30 → 1.0 at 22:00
        return "evening_grace", progress

    # Morning grace: 06:00 (360) to 06:30 (390)
    # Probability ramps from 100% at 06:00 to 0% at 06:30
    if 360 <= time_minutes < 390:
        progress = 1.0 - ((time_minutes - 360) / 30.0)  # 1.0 at 06:00 → 0.0 at 06:30
        return "morning_grace", progress

    return "awake", 0.0


class SonarrAI(ResponseMixin, ClassifierMixin, commands.Cog):
    """Main AI personality cog for the Sonarr bot."""

    # Sleep responses (hard refusal)
    SLEEP_RESPONSES = (
        "zzz... go away...",
        "im sleeping. leave.",
        "REACT:😴:Do not disturb.",
        "DOUBLE:...||i said i'm sleeping.",
        "*snores aggressively*",
        "who dares wake me... go away.",
        "REACT:💤:Sleeping. Come back later.",
        "no. it's sleepy time. go away.",
        "DOUBLE:*yawns*||no.",
        "i will timeout you if you wake me again.",
        "TIMEOUT:5m:Don't wake me up. 5 minute penalty.",
        "im literally unconscious rn leave me alone",
        "REACT:🛏️:Offline. Mentally and emotionally.",
        "it is past my bedtime. and yours. go sleep.",
        "DOUBLE:zzz||zzzzzzz",
        "i dont work nights. im not customer service.",
    )

    def __init__(self, bot):
        self.bot = bot
        self.classifier = MessageClassifier()
        self.sleep_mode_enabled = os.getenv("SLEEP_MODE_ENABLED", "False").lower() == "true"
        logger.info(f"[SonarrAI] Initialized (sleep_mode={'ON' if self.sleep_mode_enabled else 'OFF'})")

    def _clean_mentions(self, content: str) -> str:
        """
        Strip all user/role/channel mentions from message content.
        Discord mentions look like <@123456789>, <@!123456789>, <#123456789>, <@&123456789>
        """
        # Remove bot mention specifically
        content = content.replace(f"<@{self.bot.user.id}>", "").replace(f"<@!{self.bot.user.id}>", "")
        # Replace other user mentions with a generic "someone"
        content = re.sub(r"<@!?\d+>", "someone", content)
        # Remove role mentions
        content = re.sub(r"<@&\d+>", "", content)
        # Remove channel mentions
        content = re.sub(r"<#\d+>", "", content)
        # Clean up extra whitespace
        content = re.sub(r"\s+", " ", content).strip()
        return content

    async def _send_with_effects(self, response: str, message):
        """
        Parse and apply all effects from a response string, then send.
        Handles DELETE properly by sending to channel instead of replying.
        """
        effect = parse_response(response)

        # Apply side-effects (timeout, rename, reactions, delete, etc.)
        main_msg, followup = await apply_effects(effect, message, user_query=message.content)

        if main_msg is not None and main_msg.strip():
            if effect.delete_user_msg:
                # Message was deleted, can't reply to it — send to channel instead
                await message.channel.send(main_msg)
            else:
                await message.reply(main_msg, mention_author=False)

            if followup:
                await asyncio.sleep(1.5)
                await message.channel.send(followup)

    @commands.Cog.listener()
    async def on_message(self, message):
        """Bot responds using NLP classification."""
        if message.author.bot or not message.guild:
            return

        # Check if bot is mentioned or replied to
        is_bot_mentioned = (
            self.bot.user in message.mentions or
            (message.reference and message.reference.resolved and
             message.reference.resolved.author == self.bot.user)
        )

        # Only respond if bot is mentioned
        if not is_bot_mentioned:
            return

        # Ignore command messages
        if message.content.startswith("!"):
            return

        # --- SLEEP MODE CHECK ---
        if self.sleep_mode_enabled:
            state, probability = get_sleep_state()

            if state == "asleep":
                # Hard sleep: always send sleep response
                response = random.choice(self.SLEEP_RESPONSES)
                async with message.channel.typing():
                    await asyncio.sleep(random.uniform(1.0, 2.5))
                await self._send_with_effects(response, message)
                return

            elif state == "evening_grace":
                # Ramp toward sleep — probability increases toward 10PM
                if random.random() < probability:
                    from sonarr.responses import EVENING_GRACE_RESPONSES
                    if EVENING_GRACE_RESPONSES:
                        response = random.choice(EVENING_GRACE_RESPONSES)
                        async with message.channel.typing():
                            await asyncio.sleep(random.uniform(1.0, 2.0))
                        await self._send_with_effects(response, message)
                        return
                # else: fall through to normal response

            elif state == "morning_grace":
                # Just waking up — probability decreases toward 6:30AM
                if random.random() < probability:
                    from sonarr.responses import MORNING_GRACE_RESPONSES
                    if MORNING_GRACE_RESPONSES:
                        response = random.choice(MORNING_GRACE_RESPONSES)
                        async with message.channel.typing():
                            await asyncio.sleep(random.uniform(1.5, 3.0))
                        await self._send_with_effects(response, message)
                        return

        # --- NORMAL RESPONSE FLOW ---
        content_for_ai = self._clean_mentions(message.content)

        # Get reply context
        reply_msg_obj = None
        if message.reference and message.reference.resolved:
            reply_msg_obj = message.reference.resolved

        # Classify and respond
        guild_id_int = message.guild.id if message.guild else None

        async with message.channel.typing():
            response = await self.classify_and_respond_with_ai(
                content_for_ai,
                user_id=str(message.author.id),
                guild_id=guild_id_int,
                reply_context=None,
                chat_history="",
                reply_msg_obj=reply_msg_obj,
                channel_id=str(message.channel.id)
            )

        if response and response.strip():
            logger.debug(f"[OnMessage] Response: '{response[:100]}'")
            await self._send_with_effects(response, message)


async def setup(bot):
    await bot.add_cog(SonarrAI(bot))
