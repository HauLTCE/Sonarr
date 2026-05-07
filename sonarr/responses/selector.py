"""
selector.py — AI Dynamic and Premade Response Selection.

Provides a mixin to select responses from the pool based on the
classified category.
"""

import random
import logging

logger = logging.getLogger("bot")

class ResponseMixin:
    """Mixin providing response selection capabilities."""

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

    async def _pick_response_for_category(self, category: str) -> str:
        """Pick a premade response based on the category.

        The category determines WHAT pool to pick from.
        """
        from sonarr.responses import COLD_RESPONSES

        # Default fallback to cold
        pool = COLD_RESPONSES.get(category, COLD_RESPONSES.get("random", ["What?"]))

        # Try to avoid recently used responses
        if not hasattr(self, 'recent_responses'):
            self.recent_responses = []
            
        available = [r for r in pool if r not in self.recent_responses]
        if not available:
            available = pool  # fallback if we've exhausted the exact subset
            
        choice = random.choice(available)
        
        # Track history
        self.recent_responses.append(choice)
        if len(self.recent_responses) > 30:
            self.recent_responses.pop(0)
            
        return choice
