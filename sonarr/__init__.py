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
)
from .classifier import MessageClassifier
from .keywords import KEYWORD_MAP, STOPWORDS
from .responses import (
    GOSSIP_LINES,
    GOSSIP_RICH,
    GOSSIP_POOR,
    GOSSIP_BANKRUPT,
    GOSSIP_INVESTOR,
    GOSSIP_DEBTOR,
    GOSSIP_GAMBLER,
    GOSSIP_POKEMON,
    GOSSIP_LOUDMOUTH,
    GOSSIP_CRIMINAL,
    IDLE_CHAT_LINES,
    RATE_LIMIT_RESPONSES,
    ROB_REASONS,
    SLEEP_RESPONSES,
    DEBT_ENFORCEMENT_RESPONSES,
)
from .time_utils import TimeManager

__all__ = [
    # Patterns
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
    # Classifier
    "MessageClassifier",
    # Keywords
    "KEYWORD_MAP",
    "STOPWORDS",
    # Responses
    "GOSSIP_LINES",
    "GOSSIP_RICH",
    "GOSSIP_POOR",
    "GOSSIP_BANKRUPT",
    "GOSSIP_INVESTOR",
    "GOSSIP_DEBTOR",
    "GOSSIP_GAMBLER",
    "GOSSIP_POKEMON",
    "GOSSIP_LOUDMOUTH",
    "GOSSIP_CRIMINAL",
    "IDLE_CHAT_LINES",
    "RATE_LIMIT_RESPONSES",
    "ROB_REASONS",
    "SLEEP_RESPONSES",
    "DEBT_ENFORCEMENT_RESPONSES",
    # Time
    "TimeManager",
]
