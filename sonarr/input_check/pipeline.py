"""
classifier_pipeline.py — Message classification pipeline.

Provides a simplified classification flow using the async two-tier classifier.
"""

import random
import asyncio
import logging

from sonarr.responses import EMPTY_MESSAGE_RESPONSES

logger = logging.getLogger("bot")

class ClassifierMixin:
    """Mixin providing async NLP classification for SonarrAI."""

    async def classify_and_respond_with_ai(
        self,
        message_content: str,
        user_id: str = None,
        guild_id: int = None,
        reply_context: str = None,
        chat_history: str = None,
        reply_msg_obj = None,
        channel_id: str = None
    ) -> str:
        """
        Async classifier: runs the two-tier classification pipeline and maps to a response.
        """
        loop = asyncio.get_running_loop()

        word_count = await loop.run_in_executor(
            None, self.classifier.count_words, message_content
        )

        # Empty message
        if word_count == 0:
            response = random.choice(EMPTY_MESSAGE_RESPONSES)
            logger.debug(f"[Classify] EMPTY MESSAGE → {response[:50]}")
            return response

        # Keyword fast-path: confident exact/keyword hits skip the ML tiers
        # entirely (saves compute and fixes borderline cases the verifiers got
        # wrong). Only trust it if it maps to a category we actually have.
        from sonarr.input_check.keyword_prepass import keyword_prepass
        from sonarr.responses import COLD_RESPONSES
        kw_category = keyword_prepass(message_content)
        if kw_category and kw_category in COLD_RESPONSES:
            logger.info(f"[Classify] KEYWORD FAST-PATH: '{message_content[:40]}' → {kw_category}")
            return await self._pick_response_for_category(kw_category)

        # Run async two-tier classification (embedding → verifiers)
        logger.debug(f"[Classify] Starting classification for: '{message_content[:40]}'")

        category, confidence, mods = await self.classifier.async_classify(
            message_content, is_reply_to_bot=(reply_msg_obj is not None)
        )

        if not category or confidence < 2:
            # Low-confidence fallback. Prefer the broad general_aspect pool so a
            # vague input gets an on-brand reply instead of being force-fit into
            # a specific category. Guard against the key being absent.
            from sonarr.responses import COLD_RESPONSES
            if "general_aspect" in COLD_RESPONSES:
                category = "general_aspect"
            else:
                candidates = [c for c in ("social_chitchat", "user_confusion", "user_unclear")
                              if c in COLD_RESPONSES]
                category = random.choice(candidates) if candidates else "social_chitchat"

        logger.debug(f"[Classify] FINAL ({word_count}w): '{message_content[:40]}' → {category}")

        # Map to response directly
        response = await self._pick_response_for_category(category)
        logger.info(f"[BotReply] Q: '{message_content}' (cat: {category}) -> A: '{response}'")
        return response
