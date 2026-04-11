"""
engine.py - Short-term conversational context tracking.
"""
from collections import deque
from datetime import datetime, timezone
import logging

logger = logging.getLogger("bot")

class Message:
    def __init__(self, author_id: str, author_name: str, content: str, is_bot: bool = False):
        self.author_id = author_id
        self.author_name = author_name
        self.content = content
        self.is_bot = is_bot
        self.timestamp = datetime.now(timezone.utc)

class ContextEngine:
    def __init__(self, max_history: int = 15):
        # Maps channel_id to a deque of Messages
        self.channel_histories = {}
        self.max_history = max_history

    def add_message(self, channel_id: str, author_id: str, author_name: str, content: str, is_bot: bool = False):
        """Add a message to the channel's history."""
        if not content.strip():
            return
            
        if channel_id not in self.channel_histories:
            self.channel_histories[channel_id] = deque(maxlen=self.max_history)
            
        msg = Message(author_id, author_name, content, is_bot)
        self.channel_histories[channel_id].append(msg)
        
    def get_history_string(self, channel_id: str, max_messages: int = None) -> str:
        """Get formatted history string for AI prompts."""
        if channel_id not in self.channel_histories or not self.channel_histories[channel_id]:
            return ""
            
        history = list(self.channel_histories[channel_id])
        if max_messages and len(history) > max_messages:
            history = history[-max_messages:]
            
        formatted = []
        for msg in history:
            role = "Bot(Sonarr)" if msg.is_bot else f"User({msg.author_name})"
            formatted.append(f"{role}: {msg.content}")
            
        return "\n".join(formatted)

    def get_recent_messages(self, channel_id: str, max_messages: int = 15, include_bot: bool = False):
        """Return recent Message objects for programmatic inspection."""
        if channel_id not in self.channel_histories or not self.channel_histories[channel_id]:
            return []

        history = list(self.channel_histories[channel_id])
        if not include_bot:
            history = [m for m in history if not m.is_bot]

        if max_messages and len(history) > max_messages:
            history = history[-max_messages:]

        return history
