# Sonarr AI Bot Modules
# This package contains the core AI and pattern matching logic for the Sonarr bot.

from .patterns import (
    SELF_ANCHORS,
    TARGET_ANCHORS,
    THIRD_PARTY_ANCHORS,
    NEGATIVE_ACTION_WORDS,
    INSULT_WORDS,
    THREAT_WORDS,
    AFFECTION_WORDS,
    HELP_WORDS,
    QUESTION_WORDS,
    COMPLEX_PATTERNS,
    # New exports for enhanced pattern matching
    NEGATION_WORDS,
    NEGATION_PATTERN,
    INTENSIFIERS,
    INTENSIFIER_PATTERN,
    QUESTION_STARTERS,
    pattern_match,
    pattern_match_simple,
    check_negation,
    count_intensifiers,
    is_question,
    extract_third_party_subject,
)
from .classifier import MessageClassifier
from .keywords import KEYWORD_MAP, STOPWORDS
from .premade_answers import (
    GOSSIP_LINES,
    IDLE_CHAT_LINES,
    RATE_LIMIT_RESPONSES,
    IDLE_PING_MESSAGES,
)
from .time_utils import TimeManager

__all__ = [
    # Patterns - Anchors
    "SELF_ANCHORS",
    "TARGET_ANCHORS", 
    "THIRD_PARTY_ANCHORS",
    "NEGATIVE_ACTION_WORDS",
    "INSULT_WORDS",
    "THREAT_WORDS",
    "AFFECTION_WORDS",
    "HELP_WORDS",
    "QUESTION_WORDS",
    "COMPLEX_PATTERNS",
    # Patterns - Enhanced matching
    "NEGATION_WORDS",
    "NEGATION_PATTERN",
    "INTENSIFIERS",
    "INTENSIFIER_PATTERN",
    "QUESTION_STARTERS",
    "pattern_match",
    "pattern_match_simple",
    "check_negation",
    "count_intensifiers",
    "is_question",
    "extract_third_party_subject",
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
