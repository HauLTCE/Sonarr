"""
sonarr.input_check — Message parsing, classification, and pipeline logic.
"""

from .classifier import MessageClassifier, get_classifier, detect_misgendering, detect_correct_pronouns
from .keywords import KEYWORD_MAP, STOPWORDS, NEGATIVE_KEYWORDS
from .logger import log_classify, log_misgender, log_ai_call, log_trigger, log_response, log_pattern, log_error
from .pipeline import ClassifierMixin

__all__ = [
    "MessageClassifier",
    "get_classifier",
    "detect_misgendering",
    "detect_correct_pronouns",
    "KEYWORD_MAP",
    "STOPWORDS",
    "NEGATIVE_KEYWORDS",
    "log_classify",
    "log_misgender",
    "log_ai_call",
    "log_trigger",
    "log_response",
    "log_pattern",
    "log_error",
    "ClassifierMixin",
]
