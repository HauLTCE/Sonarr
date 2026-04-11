"""
brain_integration.py — Bridge between the AI brain and the response system.

Provides mixin methods for SonarrAI to process stimuli through the
Hybrid Utility AI brain and select appropriately-toned responses.
"""

import random
import logging
from datetime import datetime, timezone

from sonarr.responses import (
    COLD_RESPONSES, ESCALATED_RESPONSES, SASSY_RESPONSES
)
from sonarr.brain import Stimulus
from sonarr.brain.personality import RELATIONSHIP_EFFECTS

logger = logging.getLogger("bot")


class BrainMixin:
    """Mixin providing brain-based response selection for SonarrAI."""

    async def _brain_select_response(self, category: str, user_id: str, guild_id: int = None, user_query: str = None, chat_history: str = None) -> str:
        """Use the AI brain to select a response based on emotional state.

        Args:
            category: Classified message category (e.g. 'insult', 'greeting')
            user_id: Discord user ID as string
            guild_id: Discord guild ID

        Returns:
            Selected response string from premade answers.
        """
        # Time decay since last interaction
        now = datetime.now(timezone.utc)
        dt = (now - self._last_brain_tick).total_seconds()
        if dt > 0:
            self.brain.tick(dt)
            self._last_brain_tick = now

        # Create stimulus from classified category
        stimulus = Stimulus(
            entity_id=user_id,
            event_type=category,
            intensity=0.5,
        )

        # Process through brain pipeline
        action_result, cog_state, all_scores = self.brain.receive_stimulus_full(stimulus)

        # Update relationship based on category
        effects = RELATIONSHIP_EFFECTS.get(category, {})
        for dimension, delta in effects.items():
            self.brain.blackboard.update_relationship(user_id, dimension, delta)

        # Save state (non-blocking)
        guild_str = str(guild_id) if guild_id else "global"
        entity = self.brain.blackboard.get_entity(user_id)
        if entity:
            try:
                self.brain_persistence.save_entity(
                    user_id, guild_str, entity.relationship,
                    len(entity.interaction_history)
                )
            except Exception as e:
                logger.error(f"[Brain] Failed to save entity: {e}")

        try:
            self.brain_persistence.save_emotional_state(
                guild_str,
                self.brain.current_emotion,
                self.brain.current_mood,
            )
        except Exception as e:
            logger.error(f"[Brain] Failed to save emotion: {e}")

        # Determine action name
        action_name = action_result.action_name if action_result else "respond_cold"

        # Log brain decision
        emotion = self.brain.current_emotion
        mood = self.brain.current_mood
        label = self.brain.emotion_label
        entity_info = self.brain.blackboard.get_entity(user_id)
        rel_str = repr(entity_info.relationship) if entity_info else "unknown"
        scores_str = ", ".join(f"{s.action_name}={s.score:.3f}" for s in (all_scores or [])[:4])
        logger.debug(
            f"[Brain] user={user_id} | cat={category} | emotion={emotion} ({label}) "
            f"| mood={mood} | rel={rel_str} | scores=[{scores_str}] → {action_name}"
        )

        # Select response based on action
        return await self._pick_response_for_action(action_name, category, user_id, user_query, chat_history)

    def _strip_effects(self, response: str) -> str:
        """Strip prefixes like REACT:, DOUBLE:, TIMEOUT:, WHISPER:, DELETE: from response."""
        parts = response.split(":")
        if len(parts) >= 2 and parts[0] in ["REACT", "DOUBLE", "TIMEOUT", "WHISPER", "DELETE"]:
            # Special case for DOUBLE: which has ||
            if parts[0] == "DOUBLE" and "||" in response:
                return response.split(":", 1)[1].replace("||", " ")
            elif parts[0] == "REACT":
                return ":".join(parts[2:]) # Skip the emoji part too
            elif parts[0] == "TIMEOUT":
                return ":".join(parts[2:]) # Skip the duration part too
            else:
                return ":".join(parts[1:])
        return response

    async def _pick_response_for_action(self, action_name: str, category: str, user_id: str = None, user_query: str = None, chat_history: str = None) -> str:
        """Pick a premade response based on the brain's chosen action.

        The category determines WHAT pool to pick from.
        The action determines HOW to modify the selection.
        """
        import os
        import asyncio

        # Default fallback to cold
        pool = COLD_RESPONSES.get(category, COLD_RESPONSES.get("random", ["What?"]))

        if action_name in ["respond_escalated", "respond_grudge"]:
            pool = ESCALATED_RESPONSES.get(category, ESCALATED_RESPONSES.get("random", pool))
        elif action_name in ["respond_sassy", "respond_power_trip"]:
            pool = SASSY_RESPONSES.get(category, SASSY_RESPONSES.get("random", pool))
        elif action_name in ["respond_warm", "respond_intrigued"]:
            # Fallback to cold since warm responses were removed
            pass
        elif action_name == "ignore":
            return ""

        # Try to avoid recently used responses
        available = [r for r in pool if r not in self.recent_responses]
        if not available:
            available = pool  # fallback if we've exhausted the exact subset
            
        ai_selection_enabled = os.getenv("AI_RESPONSE_SELECTION_ENABLED", "False").lower() == "true"
        ai_dynamic_enabled = os.getenv("AI_DYNAMIC_RESPONSE_ENABLED", "False").lower() == "true"
        
        choice = None
        
        if hasattr(self, 'genai_client') and self.genai_client and getattr(self, 'ai_available', False) and user_query:
            if ai_dynamic_enabled:
                context_str = ""
                if chat_history:
                    context_str = f"CHAT HISTORY:\n{chat_history}\n\n"
                
                # Fetch long-term memories
                from sonarr.context.memory_store import memory_store
                guild_str = str(self.bot.guilds[0].id) if self.bot.guilds else "global" # Fallback
                
                memories = memory_store.get_memories(guild_str, user_id, limit=5)
                memory_str = ""
                if memories:
                    memory_str = "LONG-TERM MEMORIES WITH THIS USER:\n" + "\n".join([f"- {m['content']}" for m in memories]) + "\n\n"
                
                # Fetch brain state
                emotion = self.brain.current_emotion
                mood = self.brain.current_mood
                entity_info = self.brain.blackboard.get_entity(user_id)
                rel_str = str(entity_info.relationship) if entity_info else "Neutral"
                
                prompt = f"""You are Sonarr, an AI chatbot. You are generating a direct response to the user.
Your personality and current state must dictate how you respond.

YOUR CURRENT STATE:
Emotion: {emotion}
Mood: {mood}
Relationship with User: {rel_str}
Message Category/Intent: {category}
Action Decided: {action_name}

{memory_str}{context_str}The user just sent this message:
"{user_query}"

Generate the perfect, in-character response. Keep it natural, conversational, and tailored to your current emotional state. Do not include quotes or prefixes. Just the response text."""
                
                try:
                    response = await asyncio.wait_for(
                        self.genai_client.aio.models.generate_content(
                            model=getattr(self, 'current_model', 'gemini-2.5-flash'),
                            contents=prompt
                        ),
                        timeout=10.0
                    )
                    choice = response.text.strip()
                    logger.debug(f"[AI-Dynamic] Generated: '{choice}'")
                except Exception as e:
                    logger.warning(f"[AI-Dynamic] Failed to generate response: {e}")
                    
            elif ai_selection_enabled:
                # Prepare choices for AI
                stripped_choices = {self._strip_effects(r).strip(): r for r in available}
                choices_list_str = "\n".join([f"- {c}" for c in stripped_choices.keys() if c])
                
                if choices_list_str:
                    context_str = ""
                    if chat_history:
                        context_str = f"CHAT HISTORY (context for the conversation):\n{chat_history}\n\n"
                        
                    prompt = f"""You are selecting the best response for a chatbot to send.
{context_str}The user just sent this message:
"{user_query}"

Choose the absolute best response from the list below that matches the tone and context of the conversation. Reply ONLY with the exact text of the response you choose, nothing else. Do not add quotes.

Responses:
{choices_list_str}"""
                    
                    try:
                        response = await asyncio.wait_for(
                            self.genai_client.aio.models.generate_content(
                                model=getattr(self, 'current_model', 'gemini-2.5-flash'),
                                contents=prompt
                            ),
                            timeout=10.0
                        )
                        ai_choice = response.text.strip()
                        
                        # Match back to original (with prefixes)
                        best_match_orig = None
                        for stripped, orig in stripped_choices.items():
                            if ai_choice.lower() in stripped.lower() or stripped.lower() in ai_choice.lower():
                                best_match_orig = orig
                                break
                                
                        if best_match_orig:
                            choice = best_match_orig
                            logger.debug(f"[AI-Select] Chosen: '{choice}'")
                    except Exception as e:
                        logger.warning(f"[AI-Select] Failed to pick response: {e}")
                    
        if choice is None:
            choice = random.choice(available)
        
        # Track history
        self.recent_responses.append(choice)
        if len(self.recent_responses) > 30:
            self.recent_responses.pop(0)
            
        return choice
