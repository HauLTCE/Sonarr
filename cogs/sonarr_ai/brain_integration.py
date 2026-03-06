"""
brain_integration.py — Bridge between the AI brain and the response system.

Provides mixin methods for SonarrAI to process stimuli through the
Hybrid Utility AI brain and select appropriately-toned responses.
"""

import random
import logging
from datetime import datetime, timezone

from sonarr.premade_answers import COLD_RESPONSES
from sonarr.brain import Stimulus
from sonarr.brain.personality import RELATIONSHIP_EFFECTS

logger = logging.getLogger("bot")


class BrainMixin:
    """Mixin providing brain-based response selection for SonarrAI."""

    def _brain_select_response(self, category: str, user_id: str, guild_id: int = None) -> str:
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
        logger.info(
            f"[Brain] user={user_id} | cat={category} | emotion={emotion} ({label}) "
            f"| mood={mood} | rel={rel_str} | scores=[{scores_str}] → {action_name}"
        )

        # Select response based on action
        return self._pick_response_for_action(action_name, category)

    def _pick_response_for_action(self, action_name: str, category: str) -> str:
        """Pick a premade response based on the brain's chosen action.

        The category determines WHAT pool to pick from.
        The action determines HOW to modify the selection.
        """
        responses = COLD_RESPONSES.get(category, COLD_RESPONSES["random"])

        if action_name == "respond_cold":
            return random.choice(responses)

        elif action_name == "respond_escalated":
            sorted_responses = sorted(responses, key=len, reverse=True)
            top_half = sorted_responses[:max(len(sorted_responses) // 2, 1)]
            return random.choice(top_half)

        elif action_name == "respond_sassy":
            return random.choice(responses)

        elif action_name == "respond_warm":
            sorted_responses = sorted(responses, key=len)
            bottom_half = sorted_responses[:max(len(sorted_responses) // 2, 1)]
            return random.choice(bottom_half)

        elif action_name == "ignore":
            return ""

        elif action_name == "respond_grudge":
            grudge_pool = COLD_RESPONSES.get("sarcasm", responses)
            return random.choice(grudge_pool)

        elif action_name == "respond_power_trip":
            power_pool = COLD_RESPONSES.get("brag", responses)
            return random.choice(power_pool)

        elif action_name == "respond_intrigued":
            return random.choice(responses)

        else:
            return random.choice(responses)
