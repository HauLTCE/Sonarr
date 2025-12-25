"""
Internal command execution system for Sonarr bot.
Allows the bot to execute commands internally without Discord UI interference.
"""

import logging

logger = logging.getLogger("bot")

class InternalCommandResult:
    """Result of an internal command execution"""
    def __init__(self, success, message="", data=None):
        self.success = success
        self.message = message
        self.data = data or {}

class InternalCommandExecutor:
    """Handles internal command execution with logging"""
    
    @staticmethod
    def log_action(action: str, details: str = ""):
        """Log an action silently"""
        if details:
            logger.info(f"[INTERNAL] {action}: {details}")
        else:
            logger.info(f"[INTERNAL] {action}")
    
    @staticmethod
    def log_rob(user_name: str, amount: int, reason: str = ""):
        """Log a rob action publicly"""
        if reason:
            logger.info(f"Sonarr robbed {user_name} ${amount}. Reason: {reason}")
        else:
            logger.info(f"Sonarr robbed {user_name} ${amount}")
