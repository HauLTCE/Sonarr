"""
classifier_pipeline.py — Message classification pipeline.

Provides a simplified classification flow using the async two-tier classifier.
"""

import random
import asyncio
import logging
import re

from sonarr.responses import EMPTY_MESSAGE_RESPONSES

logger = logging.getLogger("bot")

# Categories where a TIMEOUT effect is legitimate (genuine abuse / attacks on the
# bot or others). Anywhere else, a stray TIMEOUT line is stripped so normal
# chatters don't get muted for a joke or a question. Widen this set to restore
# punishment to a category.
TIMEOUT_ALLOWED_CATEGORIES = frozenset({
    "bot_injection",
    "disruptive_hate_speech",
    "disruptive_bypass",
    "user_threat",
    "request_timeout",   # user explicitly asked to be timed out
})


def _strip_timeout_prefix(response: str) -> str:
    """Remove a leading TIMEOUT: / TIMEOUT:30m: effect, keeping the message text.

    'TIMEOUT:2m:Too personal.' -> 'Too personal.'
    'TIMEOUT:You earned that.'  -> 'You earned that.'
    """
    body = response[len("TIMEOUT:"):]
    m = re.match(r'^\d+[smhd]:(.+)$', body, re.DOTALL)
    if m:
        return m.group(1).strip()
    return body.strip()


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

        # Confidence floor. `confidence` is now the REAL match score (0.0-1.0),
        # not a hardcoded constant. Regex intent-overrides return 1.0 (certain).
        # Below the floor the classifier is essentially guessing — and a wrong
        # guess often lands on a hostile category (user_insult/user_confusion),
        # which is what was timing out / snapping at normal chatters. So route
        # anything uncertain to the broad, on-brand general_aspect pool instead
        # of committing to a bad specific category.
        CONFIDENCE_FLOOR = 0.42
        if not category or confidence < CONFIDENCE_FLOOR:
            logger.info(
                f"[Classify] LOW CONFIDENCE ({confidence if category else 'none'}) "
                f"for '{message_content[:40]}' → general_aspect fallback"
            )
            from sonarr.responses import COLD_RESPONSES
            if "general_aspect" in COLD_RESPONSES:
                category = "general_aspect"
            else:
                candidates = [c for c in ("social_chitchat", "general_aspect")
                              if c in COLD_RESPONSES]
                category = random.choice(candidates) if candidates else "social_chitchat"

        logger.debug(f"[Classify] FINAL ({word_count}w): '{message_content[:40]}' → {category}")

        # Map to response directly
        response = await self._pick_response_for_category(category)

        # Timeout gate: a TIMEOUT: effect actually mutes/punishes the user. Many
        # normal-conversation pools (jokes, questions, requests) historically had
        # stray TIMEOUT lines, so a correct classification could still randomly
        # time out a normal chatter. Only let timeouts fire for genuine-abuse
        # categories; everywhere else, keep the sassy text but strip the
        # punishment. (Reversible: widen TIMEOUT_ALLOWED_CATEGORIES to restore.)
        if response and response.startswith("TIMEOUT:") and category not in TIMEOUT_ALLOWED_CATEGORIES:
            response = _strip_timeout_prefix(response)
            logger.info(f"[Classify] Stripped stray TIMEOUT from non-abuse category '{category}'")

        logger.info(f"[BotReply] Q: '{message_content}' (cat: {category}) -> A: '{response}'")
        return response
