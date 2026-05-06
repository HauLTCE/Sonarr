"""
classifier_pipeline.py — Message classification pipeline.

Provides mixin methods for SonarrAI that handle the full classification
flow: pattern matching, cache lookup, pronoun detection, and the final decision function.
"""

import random
import asyncio
import logging
from datetime import datetime, timezone

import os
import re

from sonarr.responses import (
    COLD_RESPONSES, EMPTY_MESSAGE_RESPONSES, RATE_LIMIT_RESPONSES,
    get_gender_correction, get_callout_response,
)
from utils.database import db
from .logger import (
    log_classify, log_misgender, log_ai_call,
    log_trigger, log_response, log_pattern, log_error
)

logger = logging.getLogger("bot")


class ClassifierMixin:
    """Mixin providing classification pipeline methods for SonarrAI."""

    # ================== CLASSIFICATION STAGES ==================

    async def _classify_pattern(self, message_content: str, word_count: int) -> dict:
        """Pattern matching & Local Zero-Shot classification (runs in thread pool)."""
        try:
            loop = asyncio.get_running_loop()
            smart_cat, smart_conf = await loop.run_in_executor(
                None, self.classifier.smart_classify, message_content
            )
            modifiers = getattr(self.classifier, '_last_modifiers', {})
            return {
                "source": "pattern",
                "category": smart_cat,
                "confidence": smart_conf,
                "modifiers": modifiers,
                "word_count": word_count
            }
        except Exception as e:
            logger.error(f"[Parallel] Pattern error: {e}")
            return {"source": "pattern", "category": None, "confidence": 0, "modifiers": {}}

    async def _classify_cache(self, message_content: str, guild_id: int) -> dict:
        """Cache lookup classification (runs in thread pool for blocking DB calls)."""
        try:
            loop = asyncio.get_running_loop()

            msg_hash = await loop.run_in_executor(
                None, self.classifier.hash_message, message_content
            )
            content_words = await loop.run_in_executor(
                None, self.classifier.extract_content_words, message_content
            )

            cached_cat = await loop.run_in_executor(
                None, db.get_cached_category, msg_hash, guild_id
            )

            if cached_cat:
                return {
                    "source": "cache",
                    "category": cached_cat,
                    "confidence": 5,
                    "hash": msg_hash,
                    "words": content_words
                }

            fuzzy_cat = await loop.run_in_executor(
                None, db.fuzzy_search_category, content_words
            )
            if fuzzy_cat:
                return {
                    "source": "fuzzy",
                    "category": fuzzy_cat,
                    "confidence": 3,
                    "hash": msg_hash,
                    "words": content_words
                }

            return {"source": "cache", "category": None, "confidence": 0, "hash": msg_hash, "words": content_words}
        except Exception as e:
            logger.error(f"[Parallel] Cache error: {e}")
            return {"source": "cache", "category": None, "confidence": 0}

    async def _classify_pronouns(self, message_content: str, user_id: str, guild_id: int) -> dict:
        """Check for pronoun usage patterns (runs in thread pool for blocking operations)."""
        try:
            from .classifier import detect_correct_pronouns

            loop = asyncio.get_running_loop()
            correct_pronouns = await loop.run_in_executor(
                None, detect_correct_pronouns, message_content
            )

            if correct_pronouns:
                misgendered_users = await loop.run_in_executor(
                    None, db.get_misgendered_users, str(guild_id), 24
                )
                user_in_memory = any(mu["user_id"] == user_id for mu in misgendered_users)
                available_targets = [mu for mu in misgendered_users if mu["user_id"] != user_id]

                return {
                    "source": "pronouns",
                    "correct_usage": True,
                    "user_in_memory": user_in_memory,
                    "callout_targets": available_targets
                }
            return {"source": "pronouns", "correct_usage": False}
        except Exception as e:
            logger.error(f"[Parallel] Pronoun check error: {e}")
            return {"source": "pronouns", "correct_usage": False}


    # ================== DECISION FUNCTION ==================

    async def _decide_final_category(
        self,
        pattern_result: dict,
        cache_result: dict,
        pronoun_result: dict,
        word_count: int,
        user_id: str,
        guild_id: int
    ) -> tuple:
        """
        Decision function: combine all parallel results to pick the best category.

        Returns: (category, source, response_override)
        """
        loop = asyncio.get_running_loop()

        # Priority 1: Pronoun self-correction
        if pronoun_result.get("correct_usage") and pronoun_result.get("user_in_memory"):
            try:
                await loop.run_in_executor(
                    None, db.clear_misgendering_memory, user_id, str(guild_id)
                )
                logger.debug(f"[Decision] {user_id} used correct pronouns, cleared memory")
            except Exception:
                pass

        # Priority 2: Pronoun callout (20% chance)
        if pronoun_result.get("correct_usage"):
            targets = pronoun_result.get("callout_targets", [])
            if targets and random.random() < 0.20:
                target = targets[0]
                callout = get_callout_response(target["user_id"], target["term_used"])
                logger.debug(f"[Decision] Callout triggered for {target['user_id']}")
                return (None, "callout", callout)

        # Priority 3: High-confidence cache hit
        if cache_result.get("category") and cache_result.get("confidence", 0) >= 5:
            logger.debug(f"[Decision] Cache HIT: {cache_result['category']}")
            return (cache_result["category"], "cache", None)

        # Priority 4: Misgendering
        if pattern_result.get("category") == "misgendered":
            modifiers = pattern_result.get("modifiers", {})
            misgender_term = modifiers.get("misgendered")
            if misgender_term:
                response = get_gender_correction(misgender_term)
                if response:
                    log_misgender(
                        "", misgender_term, user_id=user_id,
                        guild_id=str(guild_id) if guild_id else None,
                        third_party=modifiers.get("third_party") is not None
                    )
                    return (None, "misgendered", response)

        # Confidence thresholds by word count
        env_threshold = os.getenv("AI_CONFIDENCE_THRESHOLD")
        if env_threshold is not None and env_threshold.strip().isdigit():
            min_confidence = int(env_threshold)
        else:
            if word_count <= 10:
                min_confidence = 2
            elif word_count <= 25:
                min_confidence = 3
            else:
                min_confidence = 4

        # Priority 5: Pattern match (which now includes Zero-Shot HuggingFace model)
        if pattern_result.get("category") and pattern_result.get("confidence", 0) >= min_confidence:
            logger.debug(f"[Decision] Pattern/ZeroShot: {pattern_result['category']} (conf={pattern_result['confidence']})")
            
            # Auto-cache the high confidence result
            try:
                msg_hash = cache_result.get("hash")
                content_words = cache_result.get("words", [])
                if msg_hash and pattern_result.get("confidence", 0) >= 3:
                    await loop.run_in_executor(
                        None,
                        lambda: db.cache_category(msg_hash, pattern_result["category"], guild_id, content_words=content_words)
                    )
            except Exception:
                pass
                
            return (pattern_result["category"], "pattern", None)

        # Priority 6: Fuzzy cache
        if cache_result.get("source") == "fuzzy" and cache_result.get("category"):
            logger.debug(f"[Decision] Fuzzy: {cache_result['category']}")
            return (cache_result["category"], "fuzzy", None)

        # Priority 8: Low-confidence pattern (short messages)
        if word_count <= 2 and pattern_result.get("category"):
            return (pattern_result["category"], "pattern_low", None)

        # Fallback
        fallback = random.choice(["disruptive_behavior", "social_chitchat", "user_confusion"])
        logger.debug(f"[Decision] Fallback: {fallback}")
        return (fallback, "fallback", None)

    # ================== MAIN CLASSIFIER ENTRY POINT ==================

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
        PARALLEL classifier: runs pattern, cache, and pronoun checks concurrently,
        then uses a decision function to pick the best result.
        """
        loop = asyncio.get_running_loop()

        word_count = await loop.run_in_executor(
            None, self.classifier.count_words, message_content
        )

        # Empty message
        if word_count == 0:
            response = random.choice(EMPTY_MESSAGE_RESPONSES)
            logger.debug(f"[Classify] EMPTY MESSAGE → {response[:50]}")
            log_classify(message_content, "empty", "empty", user_id=user_id,
                        guild_id=str(guild_id) if guild_id else None, word_count=0)
            logger.info(f"[BotReply] Q: '{message_content}' (cat: empty) -> A: '{response}'")
            return response

        # ========== PARALLEL PROCESSING ==========
        logger.debug(f"[Parallel] Starting parallel classification for: '{message_content[:40]}'")

        pattern_task = asyncio.create_task(self._classify_pattern(message_content, word_count))
        cache_task = asyncio.create_task(self._classify_cache(message_content, guild_id))
        pronoun_task = asyncio.create_task(self._classify_pronouns(message_content, user_id, guild_id))

        pattern_result, cache_result, pronoun_result = await asyncio.gather(
            pattern_task, cache_task, pronoun_task
        )

        logger.debug(f"[Parallel] Pattern/ZeroShot: {pattern_result.get('category')}, "
                     f"Cache: {cache_result.get('category')}, "
                     f"Pronoun: {pronoun_result.get('correct_usage')}")

        # ========== DECISION ==========
        category, source, response_override = await self._decide_final_category(
            pattern_result, cache_result, pronoun_result,
            word_count, user_id, guild_id
        )

        if response_override:
            logger.info(f"[BotReply] Q: '{message_content}' (cat: {category}) -> A: '{response_override}'")
            return response_override

        log_classify(
            message_content, category, source,
            confidence=pattern_result.get("confidence", 0) if source == "pattern" else None,
            user_id=user_id, guild_id=str(guild_id) if guild_id else None, word_count=word_count
        )

        logger.debug(f"[Classify] FINAL ({word_count}w, source={source}): '{message_content[:40]}' → {category}")

        # ========== DYNAMIC INTENTS (CONTEXT + MEMORY) ==========
        # These are computed here (after we have reply/channel context) rather than in the core classifier.
        if category == "know_person":
            from sonarr.systems.context.memory_store import memory_store

            target_user_id, target_name = self._extract_target_from_message(message_content, reply_msg_obj)
            if not target_user_id:
                # No target -> treat as generic question
                category = "question_general"
            else:
                guild_str = str(guild_id) if guild_id else "global"
                memories = memory_store.get_memories(guild_str, author_id=str(target_user_id), limit=50)
                mem_count = len(memories or [])

                if mem_count <= 0:
                    category = "know_person_none"
                elif mem_count <= 2:
                    category = "know_person_little"
                elif mem_count <= 9:
                    category = "know_person_has_data"
                else:
                    category = "know_person_has_history"

        elif category == "roast_someone":
            target_user_id, target_name = self._extract_target_from_message(message_content, reply_msg_obj)
            if not target_user_id:
                category = "roast_requester"
            else:
                if self._has_recent_unprovoked_bad_talk(channel_id, str(target_user_id)):
                    category = "roast_target"
                else:
                    category = "roast_requester"
        
        # Memory Action Intercept
        if category == "memory_save":
            if reply_msg_obj:
                from sonarr.systems.context.memory_store import memory_store
                topic = "general" # Could extract topic from AI
                success = memory_store.save_memory(
                    user_id=str(reply_msg_obj.author.id),
                    guild_id=str(guild_id) if guild_id else "global",
                    author_id=str(reply_msg_obj.author.id),
                    author_name=reply_msg_obj.author.display_name,
                    content=reply_msg_obj.content,
                    topic=topic
                )
                prefix = await self._brain_select_response(category, user_id, guild_id, message_content, chat_history)
                return prefix if success else "Failed to save memory."
            else:
                return "You need to reply to a message for me to save it."
            
        elif category == "memory_retrieve":
            from sonarr.systems.context.memory_store import memory_store
            target_user_id = None
            # Extract target user if mentioned
            # (Simple fallback logic for fetching memories)
            memories = memory_store.get_memories(str(guild_id) if guild_id else "global", limit=5)
            if memories:
                msgs = [f"{m['author_name']}: {m['content']}" for m in memories]
                prefix = await self._brain_select_response(category, user_id, guild_id, message_content, chat_history)
                return prefix + "\n\n" + "\n".join(msgs)
            else:
                return "I have no memory of that."

        response = await self._brain_select_response(category, user_id, guild_id, message_content, chat_history)
        logger.info(f"[BotReply] Q: '{message_content}' (cat: {category}) -> A: '{response}'")
        return response

    # ================== DYNAMIC INTENTS (CONTEXT + MEMORY) ==================

    def _extract_target_from_message(self, message_content: str, reply_msg_obj=None):
        """Extract a target discord user id/name for know_person/roast intents."""
        # Prefer reply target
        if reply_msg_obj and getattr(reply_msg_obj, "author", None):
            return str(reply_msg_obj.author.id), reply_msg_obj.author.display_name

        # Mention format <@123> or <@!123>
        mention_match = re.search(r"<@!?(\d+)>", message_content)
        if mention_match:
            return mention_match.group(1), None

        return None, None

    def _has_recent_unprovoked_bad_talk(self, channel_id: str, target_user_id: str) -> bool:
        """Check recent messages: target said something negative about Sonarr without tagging her."""
        if not channel_id or not hasattr(self, "context_engine"):
            return False
        if not target_user_id:
            return False

        recent = self.context_engine.get_recent_messages(channel_id, max_messages=15, include_bot=False)
        if not recent:
            return False

        # Tokens that count as directly addressing Sonarr.
        # Keep this strict to avoid false-positives like "you suck" aimed at other users.
        bot_tokens = {"sonarr", "sonar", "bot"}
        negative_tokens = set(__import__("sonarr.input_check.keywords", fromlist=["NEGATIVE_KEYWORDS"]).NEGATIVE_KEYWORDS)

        for msg in reversed(recent):
            if msg.author_id != str(target_user_id):
                continue

            text = msg.content.lower()
            if "<@" in text:
                # If they mentioned someone (likely the bot), treat as provoked/explicit
                continue
            words = set(re.findall(r"\b\w+\b", text))
            if words & bot_tokens and (words & negative_tokens):
                # They were talking about Sonarr explicitly
                return True

        return False
