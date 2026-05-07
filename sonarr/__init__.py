# Sonarr AI Bot Modules
# This package contains the core AI and pattern matching logic for the Sonarr bot.

from .input_check.classifier import MessageClassifier
from .input_check.keywords import KEYWORD_MAP, STOPWORDS

__all__ = [
    # Classifier
    "MessageClassifier",
    # Keywords
    "KEYWORD_MAP",
    "STOPWORDS",
]
