# Sonarr AI Bot Modules
# This package contains the core AI and pattern matching logic for the Sonarr bot.

from .classifier import MessageClassifier
from .keywords import KEYWORD_MAP, STOPWORDS
from .responses import (
    GOSSIP_LINES,
    IDLE_CHAT_LINES,
    RATE_LIMIT_RESPONSES,
    IDLE_PING_MESSAGES,
)
from .time_utils import TimeManager

__all__ = [
    # Classifier
    "MessageClassifier",
    # Keywords
    "KEYWORD_MAP",
    "STOPWORDS",
    # Responses
    "GOSSIP_LINES",
    "IDLE_CHAT_LINES",
    "RATE_LIMIT_RESPONSES",
    "IDLE_PING_MESSAGES",
    # Time
    "TimeManager",
]
