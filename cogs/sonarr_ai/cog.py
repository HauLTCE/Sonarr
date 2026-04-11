"""
cog.py — Main SonarrAI cog class.

This is the thin shell that inherits from all mixins and handles:
- Initialization (Gemini, Brain, Classifier, TimeManager)
- The on_message event listener
- Cog lifecycle (load/unload)

All heavy logic is delegated to mixins:
- BrainMixin:        _brain_select_response, _pick_response_for_action
- ClassifierMixin:   classify_and_respond_with_ai, _classify_*, _decide_*
- BackgroundTasksMixin: idle_chat_task, memory_cleanup_task, gossip
"""

import random
import logging
import os
import asyncio
import warnings
from datetime import datetime, timezone

# google-genai internally uses aiohttp and (in some versions) expects certain
# connector exceptions to be exposed at the top-level `aiohttp` module.
# Some aiohttp releases only expose them via `aiohttp.client_exceptions`.
# To keep prod stable, we provide a tiny compatibility shim.
try:
    import aiohttp  # type: ignore
    from aiohttp import client_exceptions as _aiohttp_exc  # type: ignore

    if not hasattr(aiohttp, "ClientConnectorDNSError") and hasattr(_aiohttp_exc, "ClientConnectorDNSError"):
        aiohttp.ClientConnectorDNSError = _aiohttp_exc.ClientConnectorDNSError  # type: ignore[attr-defined]
except Exception:
    # If aiohttp isn't installed for some reason, let the normal import error
    # surface where it's actually used.
    pass

import discord
from discord.ext import commands
from dotenv import load_dotenv

from sonarr.responses import COLD_RESPONSES
from sonarr.responses.effects import process_response, send_response_with_effects
from sonarr.keywords import NEGATIVE_KEYWORDS
from sonarr.classification_logger import log_trigger, log_response, log_error
from sonarr import MessageClassifier, TimeManager, KEYWORD_MAP, STOPWORDS
from sonarr.brain import AIBrain
from sonarr.brain.personality import SONARR_TRAITS, PAD_MAP, REACTIVITY, DECAY_RATE, MOOD_ALPHA
from sonarr.brain.actions import build_sonarr_actions
from sonarr.brain.persistence import BrainPersistence
from sonarr.context import ContextEngine, MemoryStore

from .brain_integration import BrainMixin
from .classifier_pipeline import ClassifierMixin
from .background_tasks import BackgroundTasksMixin

warnings.filterwarnings("ignore", message=".*google.generativeai.*")

try:
    from google import genai
except ImportError:
    genai = None

load_dotenv()

GEMINI_API_KEYS = [
    os.getenv("GEMINI_API_KEY_MAIN"),
    os.getenv("GEMINI_API_KEY_1"),
    os.getenv("GEMINI_API_KEY_2"),
    os.getenv("GEMINI_API_KEY_3"),
]
GEMINI_API_KEYS = [k for k in GEMINI_API_KEYS if k]

logger = logging.getLogger("bot")


class SonarrAI(BrainMixin, ClassifierMixin, BackgroundTasksMixin, commands.Cog):
    """Main AI personality cog for the Sonarr bot."""

    def __init__(self, bot):
        self.bot = bot

        # AI Model configuration
        self.models = [
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-3-flash-preview",
            "gemini-2.5-flash-preview-09-2025",
        ]
        self.current_model_index = 0
        self.current_key_index = 0
        self.ai_available = False
        self._gemini_keys = GEMINI_API_KEYS  # Stored for classifier_pipeline access

        # Initialize Gemini client
        if GEMINI_API_KEYS and genai:
            try:
                self.genai_client = genai.Client(api_key=GEMINI_API_KEYS[0])
                self.current_model = self.models[0]
                self.ai_available = True
                logger.info(f"[SonarrAI] Gemini initialized with key 1/{len(GEMINI_API_KEYS)}, model: {self.models[0]}")
            except Exception as e:
                logger.error(f"[SonarrAI] Failed to initialize Gemini: {e}")
                self.genai_client = None
        else:
            self.genai_client = None
            logger.warning("[SonarrAI] Gemini not available - no API keys found")

        # Initialize components
        self.classifier = MessageClassifier()
        self.time_manager = TimeManager()

        # Initialize AI Brain (state machine)
        self.brain = AIBrain(
            traits=SONARR_TRAITS,
            reactivity=REACTIVITY,
            decay_rate=DECAY_RATE,
            mood_alpha=MOOD_ALPHA,
        )
        self.brain.emotion_engine._pad_map = PAD_MAP
        for action in build_sonarr_actions():
            self.brain.utility.register_action(action)
        self.brain_persistence = BrainPersistence()
        self._last_brain_tick = datetime.now(timezone.utc)
        logger.info(f"[SonarrAI] Brain initialized: {len(self.brain.utility.action_names)} actions")

        # Context Engine
        self.context_engine = ContextEngine(max_history=15)
        from sonarr.context.memory_store import memory_store
        self.memory_store = memory_store

        # Tracking state
        self.last_idle_chat = datetime.now(timezone.utc)
        self.last_user_chat_time = {}
        self.user_ai_calls = {}
        self.user_mention_times = {}
        self.recent_responses = []

        # Start background tasks
        self.idle_chat_task.start()
        self.memory_cleanup_task.start()

    def cog_unload(self):
        """Clean up tasks when cog is unloaded."""
        self.idle_chat_task.cancel()
        self.memory_cleanup_task.cancel()

    # ================== MESSAGE EVENT ==================

    @commands.Cog.listener()
    async def on_message(self, message):
        """Bot responds using AI classification and premade cold answers."""
        logger.debug(f"[OnMessage] Received: {message.author}: {message.content[:50]}")
        if message.author.bot or not message.guild:
            return

        guild_id = str(message.guild.id)
        config = self.bot.server_config.get(guild_id, {})
        general_id = config.get("general_channel")

        # Track chat activity
        if general_id and message.channel.id == general_id:
            self.last_user_chat_time[guild_id] = datetime.now(timezone.utc)

        # Check if bot is mentioned or replied to
        is_bot_mentioned = (
            self.bot.user in message.mentions or
            (message.reference and message.reference.resolved and
             message.reference.resolved.author == self.bot.user)
        )

        # ========== SLEEP TIME (10PM - 6AM) ==========
        sleep_enabled = os.getenv("SLEEP_MODE_ENABLED", "True").lower() == "true"
        if sleep_enabled and self.time_manager.is_sleep_time():
            if is_bot_mentioned:
                await message.reply("The bot is asleep.", mention_author=False)
            return

        # ========== LUNCH BREAK (12PM - 1PM) ==========
        if self.time_manager.is_lunch_break():
            if is_bot_mentioned:
                response = self.time_manager.get_grace_response()
                await send_response_with_effects(response, message, user_query=message.content)
            return

        # Gossip trigger on user mentions
        mentioned_users = [u for u in message.mentions if u != self.bot.user and not u.bot]
        if mentioned_users and random.random() < 0.10:
            target = random.choice(mentioned_users)

            loop = asyncio.get_running_loop()
            gossip = await loop.run_in_executor(
                None,
                lambda: self.get_status_gossip(str(target.id)).format(target=target.display_name)
            )

            logger.debug(f"[Gossip] Trigger: {message.author} mentioned {target.display_name}")
            logger.debug(f"[Gossip] Response: '{gossip}'")
            await message.channel.send(gossip)
            return

        # Add to context engine
        self.context_engine.add_message(
            channel_id=str(message.channel.id),
            author_id=str(message.author.id),
            author_name=message.author.display_name,
            content=message.content,
            is_bot=False
        )

        # Only respond if bot is mentioned
        if not is_bot_mentioned:
            return

        # Ignore command messages
        if message.content.startswith("!"):
            return

        # Spam detection
        user_id = str(message.author.id)
        now = datetime.now(timezone.utc).timestamp()
        thirty_seconds_ago = now - 30

        if user_id not in self.user_mention_times:
            self.user_mention_times[user_id] = []

        self.user_mention_times[user_id] = [
            t for t in self.user_mention_times[user_id] if t > thirty_seconds_ago
        ]
        self.user_mention_times[user_id].append(now)

        if len(self.user_mention_times[user_id]) >= 5:
            logger.warning(f"[SPAM] User {message.author} mentioned bot {len(self.user_mention_times[user_id])} times in 30s")
            response = await self._brain_select_response("disruptive_spam", user_id, message.guild.id, message.content)
            try:
                await send_response_with_effects(response, message, user_query=None)
                logger.debug(f"[OnMessage] Spam response triggered")
            except Exception as e:
                logger.error(f"Error sending spam response: {e}")
            return

        content_for_ai = message.content.replace(
            f"<@{self.bot.user.id}>", ""
        ).replace(
            f"<@!{self.bot.user.id}>", ""
        ).strip()

        # ========== GRACE PERIODS ==========
        content_lower = content_for_ai.lower()
        has_curse = any(word in content_lower for word in NEGATIVE_KEYWORDS)

        if (self.time_manager.is_evening_grace() or self.time_manager.is_morning_grace()) and not has_curse:
            if self.time_manager.should_trigger_grace():
                grace_response = self.time_manager.get_grace_response()
                try:
                    grace_type = "Evening" if self.time_manager.is_evening_grace() else "Morning"
                    chance = self.time_manager.get_grace_chance()
                    logger.debug(f"[{grace_type}Grace] Triggered at {chance:.0%} chance: {message.author} said '{content_for_ai[:60]}'")
                    await send_response_with_effects(grace_response, message, user_query=content_for_ai)
                    return
                except Exception as e:
                    logger.error(f"[GracePeriod] Error: {e}")
        elif has_curse and (self.time_manager.is_evening_grace() or self.time_manager.is_morning_grace()):
            logger.debug(f"[Grace] Bypassed due to curse words in: '{content_for_ai[:60]}'")

        # Get reply context
        reply_context = None
        reply_msg_obj = None
        if message.reference and message.reference.resolved:
            ref_msg = message.reference.resolved
            reply_msg_obj = ref_msg
            reply_context = f"{ref_msg.author.display_name}: {ref_msg.content[:200]}"
            logger.debug(f"[OnMessage] Reply context: '{reply_context[:50]}...'")

        # Get chat history for AI
        chat_history = self.context_engine.get_history_string(str(message.channel.id))

        # Classify and respond
        guild_id_int = message.guild.id if message.guild else None
        logger.debug(f"[OnMessage] Calling classify_and_respond_with_ai for: '{content_for_ai}'")
        
        # We pass chat_history and reply_msg_obj to the classifier, but we need to intercept
        # macro categories if they are memory intents (done inside classify_and_respond_with_ai)
        response = await self.classify_and_respond_with_ai(
            content_for_ai,
            user_id=str(message.author.id),
            guild_id=guild_id_int,
            reply_context=reply_context,
            chat_history=chat_history,
            reply_msg_obj=reply_msg_obj,
            channel_id=str(message.channel.id)
        )
        logger.debug(f"[OnMessage] Got response: '{response[:50] if response else 'None'}'")

        try:
            main_msg, followup = await process_response(response, message, user_query=content_for_ai)

            if main_msg is not None and main_msg.strip():
                logger.debug(f"[OnMessage] Trigger: {message.author} said '{content_for_ai[:60]}'")
                logger.debug(f"[OnMessage] Response: '{main_msg[:100]}'")

                log_trigger(
                    content_for_ai,
                    str(message.author),
                    str(message.author.id),
                    guild_id=str(guild_id_int) if guild_id_int else None,
                    channel_id=str(message.channel.id)
                )
                log_response(
                    content_for_ai,
                    main_msg,
                    user_id=str(message.author.id),
                    guild_id=str(guild_id_int) if guild_id_int else None
                )

                await message.reply(main_msg, mention_author=False)
                
                # Add bot response to context engine
                self.context_engine.add_message(
                    channel_id=str(message.channel.id),
                    author_id=str(self.bot.user.id),
                    author_name=self.bot.user.display_name,
                    content=main_msg,
                    is_bot=True
                )

                if followup:
                    await asyncio.sleep(1.5)
                    await message.channel.send(followup)
                    logger.debug(f"[OnMessage] Followup: '{followup[:100]}'")
                    self.context_engine.add_message(
                        channel_id=str(message.channel.id),
                        author_id=str(self.bot.user.id),
                        author_name=self.bot.user.display_name,
                        content=followup,
                        is_bot=True
                    )
            else:
                logger.debug("[OnMessage] Skipping reply (reaction-only or empty response)")
        except Exception as e:
            logger.error(f"Error sending message: {e}")
            log_error(content_for_ai, str(e), context="on_message", user_id=str(message.author.id))
