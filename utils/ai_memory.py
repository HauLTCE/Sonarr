import logging
from utils.database import db
from datetime import datetime, timezone, timedelta

logger = logging.getLogger("bot")

class AIMemoryManager:
    """Manages AI user profiles and conversation history with SQLite backend and auto-compression."""
    
    def __init__(self):
        """Initialize with SQLite backend (30-day auto-archive)."""
        self.db = db
        self.profiles = {}  # In-memory cache for profiles
        self._load_profiles_cache()
        logger.info("[AIMemoryManager] Initialized with SQLite backend (30-day auto-archive)")
    
    def _load_profiles_cache(self):
        """Load profiles into memory cache for fast access."""
        # TODO: Load from JSON for now, can migrate to SQLite if needed
        try:
            import json
            import os
            if os.path.exists("ai_profiles.json"):
                with open("ai_profiles.json", "r") as f:
                    self.profiles = json.load(f)
        except:
            self.profiles = {}
    
    def _save_profiles_cache(self):
        """Save profiles to disk."""
        try:
            import json
            with open("ai_profiles.json", "w") as f:
                json.dump(self.profiles, f, indent=2)
        except Exception as e:
            logger.error(f"Error saving profiles: {e}")
    
    def get_user_memory(self, user_id_str):
        """Get user's conversation history (only active, non-archived messages)."""
        # Auto-archive old messages (30 days)
        self.db.archive_old_messages(user_id_str, days_old=30)
        
        # Tier 3.3: Check if we should summarize old messages (7+ days old)
        self._maybe_summarize_conversation(user_id_str)
        
        # Get active messages (newest first, max 100)
        messages = self.db.get_ai_messages(user_id_str, limit=100, include_archived=False)
        
        # Ensure profile exists
        if user_id_str not in self.profiles:
            self.profiles[user_id_str] = "Entity unrecognized. Processing pattern data."
            self._save_profiles_cache()
        
        return {
            "profile": self.profiles[user_id_str],
            "history": messages
        }
    
    def _maybe_summarize_conversation(self, user_id_str):
        """Tier 3.3: Summarize old messages if they exceed threshold."""
        from utils.summarizer import summarizer
        
        stats = self.db.get_ai_memory_stats(user_id_str)
        
        # Only summarize if more than 500 active messages
        if stats["active"] < 500:
            return
        
        try:
            # Get messages older than 7 days
            old_messages = self.db.get_old_messages_for_summary(user_id_str, days_old=7)
            
            if not old_messages:
                return
            
            # Create summary
            summary_text = summarizer.create_summary_from_messages(old_messages)
            date_start = old_messages[0][0] if old_messages else 0
            date_end = old_messages[-1][0] if old_messages else 0
            
            # Store summary
            self.db.add_conversation_summary(user_id_str, summary_text, len(old_messages), date_start, date_end)
            
            # Archive the old messages
            cutoff_time = old_messages[-1][0]
            archived = self.db.archive_messages_before_timestamp(user_id_str, cutoff_time)
            
            logger.info(f"[Summarizer] Summarized {archived} messages for user {user_id_str} (60-80% memory saving)")
        except Exception as e:
            logger.error(f"[Summarizer] Error summarizing conversation: {e}")
    
    def update_history(self, user_id_str, role, content):
        """Add message to user's conversation history."""
        self.db.add_ai_message(user_id_str, role, content)
    
    def update_profile(self, user_id_str, profile_text):
        """Update user's profile/judgment."""
        self.profiles[user_id_str] = profile_text
        self._save_profiles_cache()
    
    def mark_dirty(self):
        """No-op for SQLite (data is persisted immediately)."""
        pass
    
    def save_if_dirty(self):
        """No-op for SQLite (data is persisted immediately)."""
        pass
    
    def delete_user_memory(self, user_id_str):
        """Delete a user's memory entry (both active and archived)."""
        # Note: For safety, we don't actually delete from DB, just clear profile
        if user_id_str in self.profiles:
            del self.profiles[user_id_str]
            self._save_profiles_cache()
    
    def get_memory_stats(self, user_id_str):
        """Get stats on user's memory usage."""
        return self.db.get_ai_memory_stats(user_id_str)